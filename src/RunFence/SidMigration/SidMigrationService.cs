using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.SidMigration;

public class SidMigrationService(
    ISidResolver sidResolver,
    IProfilePathResolver profilePathResolver,
    ISidCleanupHelper sidCleanup,
    ISidAclScanService aclScan,
    ISidNameCacheService sidNameCache,
    IInteractiveUserSidResolver interactiveUserSidResolver,
    IDatabaseProvider databaseProvider,
    SidMigrationCoreMutationService coreMutationService,
    ITrackingJobStateStore? trackingJobStateStore = null) : ISidMigrationService
{
    public List<SidMigrationMapping> BuildMappings(
        IReadOnlyList<CredentialEntry> credentials,
        IReadOnlyList<LocalUserAccount> currentLocalAccounts,
        IReadOnlyDictionary<string, string>? sidNames = null)
    {
        var mappings = new List<SidMigrationMapping>();
        var interactiveUserSid = interactiveUserSidResolver.GetInteractiveUserSid();
        var accountsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in currentLocalAccounts)
            accountsByName[account.Username] = account.Sid;

        foreach (var cred in credentials)
        {
            if (cred.IsCurrentAccount
                || (!string.IsNullOrEmpty(interactiveUserSid)
                    && string.Equals(cred.Sid, interactiveUserSid, StringComparison.OrdinalIgnoreCase))
                || string.IsNullOrEmpty(cred.Sid))
                continue;

            // Check if old SID still resolves
            var resolved = sidResolver.TryResolveName(cred.Sid);
            if (resolved != null)
                continue;

            // Look up username from SidNames map
            var credUsername = sidNames != null && sidNames.TryGetValue(cred.Sid, out var name) ? name : null;
            if (string.IsNullOrEmpty(credUsername))
                continue;

            // Strip machine prefix (e.g. "MACHINE\alice" → "alice") before matching local account names
            var slashIdx = credUsername.LastIndexOf('\\');
            if (slashIdx >= 0)
                credUsername = credUsername[(slashIdx + 1)..];

            // Check if a local account with the same username exists
            if (!accountsByName.TryGetValue(credUsername, out var newSid))
                continue;
            if (string.Equals(cred.Sid, newSid, StringComparison.OrdinalIgnoreCase))
                continue;

            mappings.Add(new SidMigrationMapping(cred.Sid, newSid, credUsername));
        }

        return mappings;
    }

    public Task<List<OrphanedSid>> DiscoverOrphanedSidsAsync(
        IReadOnlyList<string> rootPaths,
        IProgress<(long scanned, long sidsFound)> onProgress,
        CancellationToken ct)
        => aclScan.DiscoverOrphanedSidsAsync(rootPaths, onProgress, ct);

    public Task<List<SidMigrationMatch>> ScanAsync(
        IReadOnlyList<string> rootPaths,
        IReadOnlyList<SidMigrationMapping> mappings,
        IReadOnlyList<string> sidsToDelete,
        IProgress<(long scanned, long found)> onProgress,
        CancellationToken ct)
        => aclScan.ScanAsync(rootPaths, mappings, sidsToDelete, onProgress, ct);

    public Task<(long applied, long errors)> ApplyAsync(
        IReadOnlyList<SidMigrationMatch> hits,
        IReadOnlyList<SidMigrationMapping> mappings,
        IReadOnlyList<string> sidsToDelete,
        IProgress<MigrationProgress> onProgress,
        CancellationToken ct)
        => aclScan.ApplyAsync(hits, mappings, sidsToDelete, onProgress, ct);

    public MigrationCounts MigrateAppData(
        IReadOnlyList<SidMigrationMapping> mappings,
        CredentialStore credentialStore)
    {
        var database = databaseProvider.GetDatabase();
        var counts = coreMutationService.ApplyCoreMappings(mappings, credentialStore, database);
        var sidMap = coreMutationService.CreateSidMap(mappings);

        // Update grant paths that reference old profile directories (e.g. C:\Users\OldName\...)
        // Only update paths that no longer exist on disk — paths that still exist are intentionally
        // shared or located on a different volume and must be left as-is.
        foreach (var mapping in mappings)
            RebaseAccountGrants(database, mapping);

        // Copy SidNames entries from old SIDs to new SIDs (only when new SID has no cached name)
        foreach (var mapping in mappings)
        {
            if (database.SidNames.TryGetValue(mapping.OldSid, out var name)
                && !database.SidNames.ContainsKey(mapping.NewSid))
                sidNameCache.UpdateName(mapping.NewSid, name);
        }

        MigrateDictionaryKeys(database.AccountGroupSnapshots, sidMap,
            mergeExisting: (existing, incoming) =>
            {
                var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
                existing.AddRange(incoming.Where(sid => existingSet.Add(sid)));
            });

        if (sidMap.TryGetValue(database.Settings.LastUsedRunAsAccountSid ?? "", out var newLast))
            database.Settings.LastUsedRunAsAccountSid = newLast;

        foreach (var mapping in mappings)
            trackingJobStateStore?.MigrateTrackingJobSid(mapping.OldSid, mapping.NewSid, saveImmediately: false);

        // JobKeeper identities are intentionally left keyed to the original SID. They are durable
        // reconnect hints for already-running keepers, not general SID-owned data to rewrite during
        // migration, and must not be copied or re-keyed onto the replacement SID here.

        return counts;
    }

    public (int credentials, int apps, int ipcCallers) DeleteSidsFromAppData(
        IReadOnlyList<string> sidsToDelete,
        CredentialStore credentialStore)
    {
        int deletedApps = 0, deletedCreds = 0, deletedCallers = 0;

        foreach (var sid in sidsToDelete)
        {
            deletedCreds += credentialStore.Credentials.RemoveAll(c =>
                string.Equals(c.Sid, sid, StringComparison.OrdinalIgnoreCase));

            // Keep any persisted JobKeeper identity under its original SID unless some other explicit
            // stale-keeper cleanup path removes it. Deletion must not synthesize a replacement SID entry.
            // CleanupSidFromAppData also removes same-transaction tracking-job SID state.
            var (removedApps, removedCallers) = sidCleanup.CleanupSidFromAppData(sid);
            deletedApps += removedApps;
            deletedCallers += removedCallers;
        }

        return (deletedCreds, deletedApps, deletedCallers);
    }

    /// <summary>
    /// Updates grant paths in the <see cref="AccountEntry"/> for <paramref name="mapping"/>.NewSid
    /// that reference the old profile directory. Only paths that no longer exist on disk are rebased.
    /// </summary>
    private void RebaseAccountGrants(AppDatabase database, SidMigrationMapping mapping)
    {
        var oldProfilePath = profilePathResolver.TryGetProfilePath(mapping.OldSid);
        var newProfilePath = profilePathResolver.TryGetProfilePath(mapping.NewSid);
        if (oldProfilePath == null || newProfilePath == null)
            return;

        var account = database.GetAccount(mapping.NewSid);
        if (account == null)
            return;

        foreach (var grant in account.Grants)
        {
            grant.Path = TryRebaseProfilePath(grant.Path, oldProfilePath, newProfilePath);

            if (grant.AllAppliedPaths != null)
            {
                for (int i = 0; i < grant.AllAppliedPaths.Count; i++)
                    grant.AllAppliedPaths[i] = TryRebaseProfilePath(grant.AllAppliedPaths[i], oldProfilePath, newProfilePath);
            }
        }
    }

    /// <summary>
    /// If <paramref name="path"/> is under <paramref name="oldBase"/> and does not exist on disk,
    /// returns the path with the old base replaced by <paramref name="newBase"/>.
    /// Otherwise returns <paramref name="path"/> unchanged.
    /// </summary>
    private string TryRebaseProfilePath(string path, string oldBase, string newBase)
    {
        bool isUnder = string.Equals(path, oldBase, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(oldBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!isUnder || Directory.Exists(path) || File.Exists(path))
            return path;
        return newBase + path[oldBase.Length..];
    }

    private static void MigrateDictionaryKeys<TValue>(
        Dictionary<string, TValue>? dict,
        IReadOnlyDictionary<string, string> sidMap,
        Action<TValue, TValue>? mergeExisting = null)
    {
        if (dict == null)
            return;

        var keysToMigrate = dict.Keys.Where(k => sidMap.ContainsKey(k)).ToList();
        foreach (var oldKey in keysToMigrate)
        {
            var newKey = sidMap[oldKey];
            var value = dict[oldKey];
            dict.Remove(oldKey);
            if (mergeExisting != null && dict.TryGetValue(newKey, out var existing))
                mergeExisting(existing, value);
            else
                dict.TryAdd(newKey, value);
        }
    }
}
