using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Persistence;

public class MainConfigImportPreservationCollector(
    GrantIntentOwnershipProjectionService ownershipProjection)
{
    public MainConfigImportPreservationSnapshot Collect(
        AppDatabase database,
        AppDatabase importedDb,
        Dictionary<string, string?>? sidResolutions)
    {
        var oldMainGrants = new Dictionary<string, List<GrantedPathEntry>>(StringComparer.OrdinalIgnoreCase);
        var additionalGrants = new Dictionary<string, List<GrantedPathEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in database.Accounts)
        {
            var main = new List<GrantedPathEntry>();
            var extra = new List<GrantedPathEntry>();
            foreach (var grant in account.Grants)
            {
                if (!ownershipProjection.HasRegisteredAdditionalConfigs ||
                    ownershipProjection.HasMainOwnership(account.Sid, grant))
                {
                    main.Add(grant);
                }

                var additionalProjection = ownershipProjection.GetAdditionalProjectionEntry(account.Sid, grant);
                if (additionalProjection != null)
                    extra.Add(additionalProjection);
            }

            if (main.Count > 0)
                oldMainGrants[account.Sid] = main;
            if (extra.Count > 0)
                additionalGrants[account.Sid] = extra;
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
            AccountsToPreserve = accountsToPreserve,
            OldSidNames = database.SidNames.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value,
                StringComparer.OrdinalIgnoreCase),
            OldContainers = [.. database.AppContainers]
        };
    }
}
