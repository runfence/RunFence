using RunFence.Core.Models;

namespace RunFence.SidMigration;

public sealed class SidMigrationCoreMutationService
{
    public MigrationCounts ApplyCoreMappings(
        IReadOnlyList<SidMigrationMapping> mappings,
        CredentialStore credentialStore,
        AppDatabase database)
    {
        var sidMap = CreateSidMap(mappings);

        var credentialCount = ApplyCredentialMappings(credentialStore, sidMap);
        var appCount = ApplyAppMappings(database, sidMap);
        var ipcCallerCount = ApplyIpcCallerMappings(database, mappings, sidMap);
        var allowEntryCount = ApplyAllowEntryMappings(database, sidMap);

        return new MigrationCounts(credentialCount, appCount, ipcCallerCount, allowEntryCount);
    }

    public Dictionary<string, string> CreateSidMap(IReadOnlyList<SidMigrationMapping> mappings)
    {
        var sidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
            sidMap[mapping.OldSid] = mapping.NewSid;
        return sidMap;
    }

    private int ApplyCredentialMappings(
        CredentialStore credentialStore,
        IReadOnlyDictionary<string, string> sidMap)
    {
        var migratedCount = 0;
        var credentialsToRemove = new List<CredentialEntry>();

        foreach (var credential in credentialStore.Credentials)
        {
            if (!sidMap.TryGetValue(credential.Sid, out var newSid))
                continue;

            if (credentialStore.Credentials.Any(candidate =>
                    candidate != credential
                    && string.Equals(candidate.Sid, newSid, StringComparison.OrdinalIgnoreCase)))
            {
                credentialsToRemove.Add(credential);
                continue;
            }

            credential.Sid = newSid;
            migratedCount++;
        }

        foreach (var credential in credentialsToRemove)
            credentialStore.Credentials.Remove(credential);

        return migratedCount;
    }

    private int ApplyAppMappings(
        AppDatabase database,
        IReadOnlyDictionary<string, string> sidMap)
    {
        var migratedCount = 0;
        foreach (var app in database.Apps)
        {
            if (!sidMap.TryGetValue(app.AccountSid, out var newSid))
                continue;

            app.AccountSid = newSid;
            migratedCount++;
        }

        return migratedCount;
    }

    private int ApplyIpcCallerMappings(
        AppDatabase database,
        IReadOnlyList<SidMigrationMapping> mappings,
        IReadOnlyDictionary<string, string> sidMap)
    {
        var migratedCount = 0;

        foreach (var app in database.Apps)
            migratedCount += ReplaceSidInStringList(app.AllowedIpcCallers, sidMap);

        foreach (var mapping in mappings)
            migratedCount += MergeAccountEntries(database, mapping);

        return migratedCount;
    }

    private int ApplyAllowEntryMappings(
        AppDatabase database,
        IReadOnlyDictionary<string, string> sidMap)
    {
        var migratedCount = 0;
        foreach (var app in database.Apps)
            migratedCount += ReplaceSidInAllowAclEntries(app.AllowedAclEntries, sidMap);

        return migratedCount;
    }

    private int ReplaceSidInStringList(
        List<string>? items,
        IReadOnlyDictionary<string, string> sidMap)
    {
        if (items == null)
            return 0;

        var count = 0;
        for (var i = 0; i < items.Count; i++)
        {
            if (!sidMap.TryGetValue(items[i], out var newSid))
                continue;

            items[i] = newSid;
            count++;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        items.RemoveAll(item => !seen.Add(item));
        return count;
    }

    private int ReplaceSidInAllowAclEntries(
        List<AllowAclEntry>? items,
        IReadOnlyDictionary<string, string> sidMap)
    {
        if (items == null)
            return 0;

        var count = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!sidMap.TryGetValue(item.Sid, out var newSid))
                continue;

            items[i] = item with { Sid = newSid };
            count++;
        }

        var firstIndexBySid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < items.Count;)
        {
            var sid = items[i].Sid;
            if (!firstIndexBySid.TryGetValue(sid, out var firstIndex))
            {
                firstIndexBySid[sid] = i;
                i++;
                continue;
            }

            var first = items[firstIndex];
            var duplicate = items[i];
            items[firstIndex] = first with
            {
                AllowExecute = first.AllowExecute || duplicate.AllowExecute,
                AllowWrite = first.AllowWrite || duplicate.AllowWrite
            };
            items.RemoveAt(i);
        }

        return count;
    }

    private int MergeAccountEntries(AppDatabase database, SidMigrationMapping mapping)
    {
        var oldEntry = database.GetAccount(mapping.OldSid);
        if (oldEntry == null)
            return 0;

        var existingNew = database.GetAccount(mapping.NewSid);
        if (existingNew != null)
        {
            existingNew.IsIpcCaller |= oldEntry.IsIpcCaller;
            existingNew.TrayFolderBrowser |= oldEntry.TrayFolderBrowser;
            existingNew.TrayDiscovery |= oldEntry.TrayDiscovery;
            existingNew.TrayTerminal |= oldEntry.TrayTerminal;
            if (GetPrivilegeMergeRank(oldEntry.PrivilegeLevel) > GetPrivilegeMergeRank(existingNew.PrivilegeLevel))
                existingNew.PrivilegeLevel = oldEntry.PrivilegeLevel;
            if (oldEntry.DeleteAfterUtc.HasValue &&
                (!existingNew.DeleteAfterUtc.HasValue || oldEntry.DeleteAfterUtc < existingNew.DeleteAfterUtc))
            {
                existingNew.DeleteAfterUtc = oldEntry.DeleteAfterUtc;
            }

            foreach (var grant in oldEntry.Grants)
            {
                if (!existingNew.Grants.Any(candidate =>
                        string.Equals(candidate.Path, grant.Path, StringComparison.OrdinalIgnoreCase)
                        && candidate.IsDeny == grant.IsDeny
                        && candidate.IsTraverseOnly == grant.IsTraverseOnly))
                {
                    existingNew.Grants.Add(grant);
                }
            }

            if (existingNew.Firewall.IsDefault && !oldEntry.Firewall.IsDefault)
                existingNew.Firewall = oldEntry.Firewall;

            database.Accounts.Remove(oldEntry);
        }
        else
        {
            oldEntry.Sid = mapping.NewSid;
        }

        return oldEntry.IsIpcCaller || existingNew?.IsIpcCaller == true ? 1 : 0;
    }

    private static int GetPrivilegeMergeRank(PrivilegeLevel privilegeLevel) => privilegeLevel switch
    {
        PrivilegeLevel.HighestAllowed => 5,
        PrivilegeLevel.HighIntegrity => 4,
        PrivilegeLevel.Basic => 3,
        PrivilegeLevel.LowIntegrity => 2,
        PrivilegeLevel.Isolated => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(privilegeLevel), privilegeLevel, null)
    };
}
