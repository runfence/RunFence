using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.SidMigration;

public class SidMigrationService(
    ISidResolver sidResolver,
    ISidCleanupHelper sidCleanup,
    SidAclScanService aclScan,
    ISidNameCacheService sidNameCache,
    IDatabaseProvider databaseProvider) : ISidMigrationService
{
    public List<SidMigrationMapping> BuildMappings(
        IReadOnlyList<CredentialEntry> credentials,
        IReadOnlyList<LocalUserAccount> currentLocalAccounts,
        IReadOnlyDictionary<string, string>? sidNames = null)
    {
        var mappings = new List<SidMigrationMapping>();
        var accountsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in currentLocalAccounts)
            accountsByName[account.Username] = account.Sid;

        foreach (var cred in credentials)
        {
            if (cred.IsCurrentAccount || cred.IsInteractiveUser || string.IsNullOrEmpty(cred.Sid))
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
        IProgress<(long scanned, long found)> onProgress,
        CancellationToken ct)
        => aclScan.ScanAsync(rootPaths, mappings, onProgress, ct);

    public Task<(long applied, long errors)> ApplyAsync(
        IReadOnlyList<SidMigrationMatch> hits,
        IReadOnlyList<SidMigrationMapping> mappings,
        IProgress<MigrationProgress> onProgress,
        CancellationToken ct)
        => aclScan.ApplyAsync(hits, mappings, onProgress, ct);

    public MigrationCounts MigrateAppData(
        IReadOnlyList<SidMigrationMapping> mappings,
        CredentialStore credentialStore)
    {
        var database = databaseProvider.GetDatabase();
        var sidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mappings)
            sidMap[m.OldSid] = m.NewSid;

        int credCount = 0, appCount = 0, ipcCount = 0, allowCount = 0;

        foreach (var entry in credentialStore.Credentials)
        {
            if (sidMap.TryGetValue(entry.Sid, out var newSid))
            {
                // Skip if a credential with the target SID already exists (avoid duplicates)
                if (credentialStore.Credentials.Any(c =>
                        c != entry && string.Equals(c.Sid, newSid, StringComparison.OrdinalIgnoreCase)))
                    continue;
                entry.Sid = newSid;
                credCount++;
            }
        }

        foreach (var app in database.Apps)
        {
            if (sidMap.TryGetValue(app.AccountSid, out var newSid))
            {
                app.AccountSid = newSid;
                appCount++;
            }
        }

        foreach (var app in database.Apps)
        {
            ipcCount += ReplaceSidInStringList(app.AllowedIpcCallers, sidMap);
            allowCount += ReplaceSidInAllowAclEntries(app.AllowedAclEntries, sidMap);
        }

        // Migrate AccountEntry SIDs
        foreach (var mapping in mappings)
        {
            var oldEntry = database.GetAccount(mapping.OldSid);
            if (oldEntry == null)
                continue;

            var existingNew = database.GetAccount(mapping.NewSid);
            if (existingNew != null)
            {
                // Merge: OR booleans, merge Grants, keep earliest DeleteAfterUtc
                existingNew.IsIpcCaller |= oldEntry.IsIpcCaller;
                existingNew.TrayFolderBrowser |= oldEntry.TrayFolderBrowser;
                existingNew.TrayDiscovery |= oldEntry.TrayDiscovery;
                existingNew.TrayTerminal |= oldEntry.TrayTerminal;
                existingNew.LowIntegrityDefault |= oldEntry.LowIntegrityDefault;
                existingNew.SplitTokenOptOut |= oldEntry.SplitTokenOptOut;
                if (oldEntry.DeleteAfterUtc.HasValue &&
                    (!existingNew.DeleteAfterUtc.HasValue || oldEntry.DeleteAfterUtc < existingNew.DeleteAfterUtc))
                    existingNew.DeleteAfterUtc = oldEntry.DeleteAfterUtc;
                foreach (var g in oldEntry.Grants)
                {
                    if (!existingNew.Grants.Any(e => string.Equals(e.Path, g.Path, StringComparison.OrdinalIgnoreCase)
                                                     && e.IsDeny == g.IsDeny && e.IsTraverseOnly == g.IsTraverseOnly))
                        existingNew.Grants.Add(g);
                }

                // Merge firewall: use non-default if one exists
                if (!existingNew.Firewall.IsDefault && !oldEntry.Firewall.IsDefault)
                {
                    // Both non-default: keep existing (conservative)
                }
                else if (!oldEntry.Firewall.IsDefault)
                {
                    existingNew.Firewall = oldEntry.Firewall;
                }

                database.Accounts.Remove(oldEntry);
            }
            else
            {
                oldEntry.Sid = mapping.NewSid;
            }

            ipcCount++;
        }

        // Copy SidNames entries from old SIDs to new SIDs
        foreach (var mapping in mappings)
        {
            if (database.SidNames.TryGetValue(mapping.OldSid, out var name))
                sidNameCache.UpdateName(mapping.NewSid, name);
        }

        MigrateDictionaryKeys(database.AccountGroupSnapshots, sidMap,
            mergeExisting: (existing, incoming) =>
            {
                var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
                existing.AddRange(incoming.Where(sid => existingSet.Add(sid)));
            });

        return new MigrationCounts(credCount, appCount, ipcCount, allowCount);
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

            var (removedApps, removedCallers) = sidCleanup.CleanupSidFromAppData(sid);
            deletedApps += removedApps;
            deletedCallers += removedCallers;
        }

        return (deletedCreds, deletedApps, deletedCallers);
    }

    private static int ReplaceSidInStringList(List<string>? items, IReadOnlyDictionary<string, string> sidMap)
    {
        if (items == null)
            return 0;

        int count = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (sidMap.TryGetValue(items[i], out var newSid))
            {
                items[i] = newSid;
                count++;
            }
        }

        // Deduplicate by SID (keep first occurrence)
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        items.RemoveAll(s => !seen.Add(s));

        return count;
    }

    private static int ReplaceSidInAllowAclEntries(List<AllowAclEntry>? items, IReadOnlyDictionary<string, string> sidMap)
    {
        if (items == null)
            return 0;

        int count = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (sidMap.TryGetValue(item.Sid, out var newSid))
            {
                items[i] = item with { Sid = newSid };
                count++;
            }
        }

        // Deduplicate by SID (keep first occurrence)
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < items.Count;)
        {
            if (!seen.Add(items[i].Sid))
                items.RemoveAt(i);
            else
                i++;
        }

        return count;
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