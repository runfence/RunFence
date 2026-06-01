using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Persistence;

public class MainConfigImportApplyService(
    IAppConfigService appConfigService,
    MainConfigImportRepairService repairService,
    GrantIntentOwnershipProjectionService ownershipProjection,
    IGrantInspectionService grantInspection)
{
    public void ApplyState(
        AppDatabase database,
        AppDatabase importedDb,
        MainConfigImportRepairPlan repairPlan,
        MainConfigImportPreservationSnapshot preservation,
        Dictionary<string, string?>? sidResolutions)
    {
        ReplaceAppsAndSettings(database, importedDb, repairPlan.AdditionalApps);
        repairService.ApplyAdditionalAppIdRepairs(repairPlan);
        ReplaceContainers(database, importedDb, preservation.OldContainers);
        ReplaceSidNames(database, importedDb, preservation.OldSidNames, sidResolutions);
        ReplaceAccountsAndRestoreMainGrants(database, importedDb, preservation, repairPlan.OrphanedGrantSids);
        ownershipProjection.ReplaceMainOwnership(database.Accounts);
        RespliceAdditionalConfigGrants(database, preservation.AdditionalGrants);
        ApplyCleanupAndDefaults(database);
    }

    private void ReplaceAppsAndSettings(
        AppDatabase database,
        AppDatabase importedDb,
        List<AppEntry> additionalApps)
    {
        var priorNagEligible = database.Settings.NagEligible;
        database.Apps.Clear();
        database.Apps.AddRange(importedDb.Apps);
        database.Apps.AddRange(additionalApps);

        database.Settings = importedDb.Settings ?? new AppSettings();
        database.Settings.NagEligible = priorNagEligible || database.Settings.NagEligible;
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

    private void ReplaceAccountsAndRestoreMainGrants(
        AppDatabase database,
        AppDatabase importedDb,
        MainConfigImportPreservationSnapshot preservation,
        IEnumerable<string> orphanedGrantSids)
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

        RestoreMainConfigGrants(database, preservation.OldMainGrants, orphanedGrantSids);
    }

    private void RestoreMainConfigGrants(
        AppDatabase database,
        IReadOnlyDictionary<string, List<GrantedPathEntry>> oldMainGrants,
        IEnumerable<string> orphanedGrantSids)
    {
        var orphanedGrantSidSet = orphanedGrantSids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (sid, grants) in oldMainGrants)
        {
            if (orphanedGrantSidSet.Contains(sid))
                continue;

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
                var identity = GrantIntentEntryIdentity.From(sid, grant);
                if (!account.Grants.Any(existingGrant => GrantIntentEntryIdentity.From(sid, existingGrant) == identity))
                {
                    account.Grants.Add(grant);
                }
            }
        }
    }

    private void ApplyCleanupAndDefaults(AppDatabase database)
    {
        foreach (var account in database.Accounts.ToList())
            database.RemoveAccountIfEmpty(account.Sid);

        WellKnownAccountDefaults.Apply(database);
    }

}
