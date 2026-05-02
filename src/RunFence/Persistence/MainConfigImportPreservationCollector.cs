using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Persistence;

public class MainConfigImportPreservationCollector(IGrantConfigTracker grantTracker)
{
    public MainConfigImportPreservationSnapshot Collect(
        AppDatabase database,
        AppDatabase importedDb,
        Dictionary<string, string?>? sidResolutions)
    {
        var oldMainGrants = new Dictionary<string, List<GrantedPathEntry>>(StringComparer.OrdinalIgnoreCase);
        var additionalGrants = new Dictionary<string, List<GrantedPathEntry>>(StringComparer.OrdinalIgnoreCase);
        var oldMainSharedContainerTraverseGrants = new List<GrantedPathEntry>();
        var additionalSharedContainerTraverseGrants = new List<GrantedPathEntry>();
        foreach (var account in database.Accounts)
        {
            var main = new List<GrantedPathEntry>();
            var extra = new List<GrantedPathEntry>();
            foreach (var grant in account.Grants)
            {
                if (grantTracker.IsInMainConfig(account.Sid, grant))
                    main.Add(grant);
                else
                    extra.Add(grant);
            }

            if (main.Count > 0)
                oldMainGrants[account.Sid] = main;
            if (extra.Count > 0)
                additionalGrants[account.Sid] = extra;
        }

        foreach (var grant in database.SharedContainerTraverseGrants)
        {
            if (grantTracker.IsInMainConfig(WellKnownSecuritySids.AllApplicationPackagesSid, grant))
                oldMainSharedContainerTraverseGrants.Add(grant);
            else
                additionalSharedContainerTraverseGrants.Add(grant);
        }

        var importedSids = new HashSet<string>(
            (importedDb.Accounts ?? []).Select(account => account.Sid),
            StringComparer.OrdinalIgnoreCase);

        var accountsToPreserve = database.Accounts
            .Where(account => !importedSids.Contains(account.Sid) &&
                              sidResolutions != null &&
                              sidResolutions.TryGetValue(account.Sid, out var resolvedName) &&
                              resolvedName != null)
            .ToList();

        return new MainConfigImportPreservationSnapshot
        {
            OldMainGrants = oldMainGrants,
            AdditionalGrants = additionalGrants,
            OldMainSharedContainerTraverseGrants = oldMainSharedContainerTraverseGrants,
            AdditionalSharedContainerTraverseGrants = additionalSharedContainerTraverseGrants,
            AccountsToPreserve = accountsToPreserve,
            OldSidNames = database.SidNames.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value,
                StringComparer.OrdinalIgnoreCase),
            OldContainers = [.. database.AppContainers]
        };
    }
}
