using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Persistence;

public class MainConfigImportApplyService(
    IAppConfigService appConfigService,
    IGrantInspectionService grantInspection)
{
    public void Apply(
        AppDatabase database,
        AppDatabase importedDb,
        List<AppEntry> additionalApps,
        MainConfigImportPreservationSnapshot preservation,
        Dictionary<string, string?>? sidResolutions)
    {
        ReplaceAppsAndSettings(database, importedDb, additionalApps);
        ReplaceContainers(database, importedDb, preservation.OldContainers);
        ReplaceSidNames(database, importedDb, preservation.OldSidNames, sidResolutions);
        ReplaceAccountsAndRespliceGrants(database, importedDb, preservation);
        ReplaceSharedContainerTraverseGrants(database, importedDb, preservation);
        ApplyCleanupAndDefaults(database);
    }

    private void ReplaceAppsAndSettings(
        AppDatabase database,
        AppDatabase importedDb,
        List<AppEntry> additionalApps)
    {
        database.Apps.Clear();
        database.Apps.AddRange(importedDb.Apps);
        database.Apps.AddRange(additionalApps);

        database.Settings = importedDb.Settings ?? new AppSettings();
        database.ShowSystemInRunAs = importedDb.ShowSystemInRunAs;
    }

    private void ReplaceContainers(
        AppDatabase database,
        AppDatabase importedDb,
        List<AppContainerEntry> oldContainers)
    {
        database.AppContainers.Clear();
        database.AppContainers.AddRange(importedDb.AppContainers);

        var additionalContainerNames = new HashSet<string>(
            database.Apps
                .Where(app => appConfigService.GetConfigPath(app.Id) != null && app.AppContainerName != null)
                .Select(app => app.AppContainerName!),
            StringComparer.OrdinalIgnoreCase);
        foreach (var container in oldContainers)
        {
            if (additionalContainerNames.Contains(container.Name) &&
                !database.AppContainers.Any(existing =>
                    string.Equals(existing.Name, container.Name, StringComparison.OrdinalIgnoreCase)))
            {
                database.AppContainers.Add(container);
            }
        }
    }

    private void ReplaceSidNames(
        AppDatabase database,
        AppDatabase importedDb,
        IReadOnlyDictionary<string, string> oldSidNames,
        Dictionary<string, string?>? sidResolutions)
    {
        database.SidNames.Clear();
        foreach (var (sid, name) in importedDb.SidNames)
            database.SidNames[sid] = name;

        if (sidResolutions == null)
            return;

        foreach (var (sid, name) in oldSidNames)
        {
            if (!database.SidNames.ContainsKey(sid) &&
                sidResolutions.TryGetValue(sid, out var resolvedName) &&
                resolvedName != null)
            {
                database.SidNames[sid] = name;
            }
        }
    }

    private void ReplaceAccountsAndRespliceGrants(
        AppDatabase database,
        AppDatabase importedDb,
        MainConfigImportPreservationSnapshot preservation)
    {
        database.Accounts.Clear();
        foreach (var importedAccount in importedDb.Accounts ?? [])
            database.Accounts.Add(importedAccount.Clone());

        foreach (var oldAccount in preservation.AccountsToPreserve)
        {
            var stub = oldAccount.Clone();
            stub.Grants.Clear();
            database.Accounts.Add(stub);
        }

        RestoreMainConfigGrants(database, preservation.OldMainGrants);
        RespliceAdditionalConfigGrants(database, preservation.AdditionalGrants);
    }

    private void RestoreMainConfigGrants(
        AppDatabase database,
        IReadOnlyDictionary<string, List<GrantedPathEntry>> oldMainGrants)
    {
        foreach (var (sid, grants) in oldMainGrants)
        {
            foreach (var grant in grants)
            {
                var isDenyForCheck = !grant.IsTraverseOnly && grant.IsDeny;
                if (grantInspection.CheckGrantStatus(grant.Path, sid, isDenyForCheck) != PathAclStatus.Available)
                    continue;

                var account = database.GetOrCreateAccount(sid);
                var existing = account.Grants.FirstOrDefault(existingGrant =>
                    string.Equals(existingGrant.Path, grant.Path, StringComparison.OrdinalIgnoreCase) &&
                    existingGrant.IsDeny == grant.IsDeny &&
                    existingGrant.IsTraverseOnly == grant.IsTraverseOnly);
                if (existing != null)
                {
                    grant.SavedRights = existing.SavedRights;
                    account.Grants.Remove(existing);
                }

                account.Grants.Add(grant);
            }
        }
    }

    private void RespliceAdditionalConfigGrants(
        AppDatabase database,
        IReadOnlyDictionary<string, List<GrantedPathEntry>> additionalGrants)
    {
        foreach (var (sid, grants) in additionalGrants)
        {
            var account = database.GetOrCreateAccount(sid);
            foreach (var grant in grants)
            {
                if (!account.Grants.Any(existingGrant =>
                        string.Equals(existingGrant.Path, grant.Path, StringComparison.OrdinalIgnoreCase) &&
                        existingGrant.IsDeny == grant.IsDeny &&
                        existingGrant.IsTraverseOnly == grant.IsTraverseOnly))
                {
                    account.Grants.Add(grant);
                }
            }
        }
    }

    private void ReplaceSharedContainerTraverseGrants(
        AppDatabase database,
        AppDatabase importedDb,
        MainConfigImportPreservationSnapshot preservation)
    {
        database.SharedContainerTraverseGrants.Clear();
        foreach (var grant in importedDb.SharedContainerTraverseGrants.Select(g => g.Clone()))
            AddSharedContainerTraverseGrantIfMissing(database, grant);

        RestoreMainConfigSharedContainerTraverseGrants(database, preservation.OldMainSharedContainerTraverseGrants);
        RespliceAdditionalSharedContainerTraverseGrants(database, preservation.AdditionalSharedContainerTraverseGrants);
    }

    private void RestoreMainConfigSharedContainerTraverseGrants(
        AppDatabase database,
        IEnumerable<GrantedPathEntry> oldMainSharedGrants)
    {
        foreach (var grant in oldMainSharedGrants)
        {
            if (grantInspection.CheckGrantStatus(
                    grant.Path,
                    WellKnownSecuritySids.AllApplicationPackagesSid,
                    isDeny: false) != PathAclStatus.Available)
                continue;

            AddSharedContainerTraverseGrantIfMissing(database, grant);
        }
    }

    private static void RespliceAdditionalSharedContainerTraverseGrants(
        AppDatabase database,
        IEnumerable<GrantedPathEntry> additionalSharedGrants)
    {
        foreach (var grant in additionalSharedGrants)
            AddSharedContainerTraverseGrantIfMissing(database, grant);
    }

    private static void AddSharedContainerTraverseGrantIfMissing(AppDatabase database, GrantedPathEntry grant)
    {
        if (!database.SharedContainerTraverseGrants.Any(existingGrant =>
                string.Equals(existingGrant.Path, grant.Path, StringComparison.OrdinalIgnoreCase) &&
                existingGrant.IsDeny == grant.IsDeny &&
                existingGrant.IsTraverseOnly == grant.IsTraverseOnly))
        {
            database.SharedContainerTraverseGrants.Add(grant);
        }
    }

    private void ApplyCleanupAndDefaults(AppDatabase database)
    {
        foreach (var account in database.Accounts.ToList())
            database.RemoveAccountIfEmpty(account.Sid);

        WellKnownAccountDefaults.Apply(database);
    }
}
