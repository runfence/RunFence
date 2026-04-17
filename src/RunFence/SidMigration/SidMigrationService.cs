using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.SidMigration;

public class SidMigrationService(
    ISidResolver sidResolver,
    ISidCleanupHelper sidCleanup,
    ISidAclScanService aclScan,
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

        var credentialsToRemove = new List<CredentialEntry>();
        foreach (var entry in credentialStore.Credentials)
        {
            if (sidMap.TryGetValue(entry.Sid, out var newSid))
            {
                // If a credential for the new SID already exists, remove the orphaned old-SID credential
                // rather than leaving it as an unresolvable SID in the credential store (e.g. RunAs dialog).
                if (credentialStore.Credentials.Any(c =>
                        c != entry && string.Equals(c.Sid, newSid, StringComparison.OrdinalIgnoreCase)))
                {
                    credentialsToRemove.Add(entry);
                    continue;
                }
                entry.Sid = newSid;
                credCount++;
            }
        }
        foreach (var orphan in credentialsToRemove)
            credentialStore.Credentials.Remove(orphan);

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
            ipcCount += MergeAccountEntries(database, mapping);

        // Update grant paths that reference old profile directories (e.g. C:\Users\OldName\...)
        // Only update paths that no longer exist on disk — paths that still exist are intentionally
        // shared or located on a different volume and must be left as-is.
        foreach (var mapping in mappings)
            RebaseAccountGrants(database, mapping);

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

        if (sidMap.TryGetValue(database.Settings.LastUsedRunAsAccountSid ?? "", out var newLast))
            database.Settings.LastUsedRunAsAccountSid = newLast;

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

        // Deduplicate by SID: when two entries share the same SID after migration,
        // OR their boolean permission flags together into the first occurrence instead of
        // discarding the second entry (which may carry additional permissions).
        var firstIndexBySid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < items.Count;)
        {
            var sid = items[i].Sid;
            if (firstIndexBySid.TryGetValue(sid, out var firstIdx))
            {
                // Merge boolean flags into the first occurrence
                var first = items[firstIdx];
                var duplicate = items[i];
                items[firstIdx] = first with
                {
                    AllowExecute = first.AllowExecute || duplicate.AllowExecute,
                    AllowWrite = first.AllowWrite || duplicate.AllowWrite
                };
                items.RemoveAt(i);
            }
            else
            {
                firstIndexBySid[sid] = i;
                i++;
            }
        }

        return count;
    }

    /// <summary>
    /// Migrates the <see cref="AccountEntry"/> for <paramref name="mapping"/>.OldSid to the
    /// new SID in <paramref name="database"/>. When an entry for the new SID already exists,
    /// boolean flags are OR-merged, grants are union-merged, firewall keeps the non-default side,
    /// and the old entry is removed. Returns 1 if the migrated entry is (or becomes) an IPC caller,
    /// otherwise 0.
    /// </summary>
    private static int MergeAccountEntries(AppDatabase database, SidMigrationMapping mapping)
    {
        var oldEntry = database.GetAccount(mapping.OldSid);
        if (oldEntry == null)
            return 0;

        var existingNew = database.GetAccount(mapping.NewSid);
        if (existingNew != null)
        {
            // Merge: OR booleans, merge Grants, keep earliest DeleteAfterUtc
            existingNew.IsIpcCaller |= oldEntry.IsIpcCaller;
            existingNew.TrayFolderBrowser |= oldEntry.TrayFolderBrowser;
            existingNew.TrayDiscovery |= oldEntry.TrayDiscovery;
            existingNew.TrayTerminal |= oldEntry.TrayTerminal;
            if ((int)oldEntry.PrivilegeLevel > (int)existingNew.PrivilegeLevel)
                existingNew.PrivilegeLevel = oldEntry.PrivilegeLevel;
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

        return oldEntry.IsIpcCaller || existingNew?.IsIpcCaller == true ? 1 : 0;
    }

    /// <summary>
    /// Updates grant paths in the <see cref="AccountEntry"/> for <paramref name="mapping"/>.NewSid
    /// that reference the old profile directory. Only paths that no longer exist on disk are rebased.
    /// </summary>
    private void RebaseAccountGrants(AppDatabase database, SidMigrationMapping mapping)
    {
        var oldProfilePath = sidResolver.TryGetProfilePath(mapping.OldSid);
        var newProfilePath = sidResolver.TryGetProfilePath(mapping.NewSid);
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
        if (!isUnder)
            return path;
        if (Directory.Exists(path) || File.Exists(path))
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