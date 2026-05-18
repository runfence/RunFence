using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

public sealed class AclBulkScanResultProcessor(
    IDatabaseProvider databaseProvider) : IAclBulkScanResultProcessor
{
    public Dictionary<string, AccountScanResult> FilterManagedPaths(
        Dictionary<string, AccountScanResult> results,
        IReadOnlyList<AppEntry> apps,
        IAclService aclService)
    {
        var managedPaths = apps
            .Where(a => a is { RestrictAcl: true, IsUrlScheme: false })
            .Select(a => AclHelper.NormalizePath(aclService.ResolveAclTargetPath(a)))
            .ToList();

        if (managedPaths.Count == 0)
            return results;

        bool IsManaged(string path) => managedPaths.Any(m => AclHelper.PathIsAtOrBelow(path, m));

        var filtered = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sid, result) in results)
        {
            var grants = result.Grants.Where(g => !IsManaged(g.Path)).ToList();
            var traverses = result.TraversePaths.Where(p => !IsManaged(p)).ToList();
            if (grants.Count > 0 || traverses.Count > 0)
                filtered[sid] = new AccountScanResult(grants, traverses);
        }

        return filtered;
    }

    public AclBulkScanImportSummary ApplyScanResults(
        Dictionary<string, AccountScanResult> selected,
        Action saveDatabase)
    {
        var database = databaseProvider.GetDatabase();
        var importedCount = 0;
        var updatedCount = 0;
        var skippedOppositeModeConflictPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (sid, result) in selected)
        {
            var grants = database.GetOrCreateAccount(sid).Grants;

            var grantsByPath = result.Grants
                .Select(grant =>
                {
                    var savedRights = grant.IsDeny
                        ? SavedRightsState.DefaultForMode(true) with { Execute = grant.Execute, Read = grant.Read }
                        : SavedRightsState.DefaultForMode(false, own: grant.IsOwner) with
                        {
                            Execute = grant.Execute,
                            Write = grant.Write,
                            Special = grant.Special
                        };
                    return new ScannedGrant(
                        AclHelper.NormalizePath(grant.Path),
                        grant.IsDeny,
                        AclHelper.ClearBlockedGrantOwner(sid, savedRights)!);
                })
                .GroupBy(grant => grant.Path, StringComparer.OrdinalIgnoreCase);

            foreach (var pathGroup in grantsByPath)
            {
                var scannedModes = pathGroup
                    .GroupBy(grant => grant.IsDeny)
                    .ToDictionary(group => group.Key, group => group.Last());
                var allowAndDenyWereBothScanned = scannedModes.Count > 1;

                foreach (var scannedGrant in scannedModes
                             .OrderBy(pair => pair.Key ? 1 : 0)
                             .Select(pair => pair.Value))
                {
                    var conflict = GrantCoreOperations.FindNonTraverseGrantConflict(
                        grants,
                        scannedGrant.Path,
                        scannedGrant.IsDeny);
                    if (conflict.SameModeEntry != null)
                    {
                        if (conflict.SameModeEntry.SavedRights != scannedGrant.SavedRights)
                        {
                            conflict.SameModeEntry.SavedRights = scannedGrant.SavedRights;
                            updatedCount++;
                        }

                        continue;
                    }

                    if (conflict.OppositeModeEntry != null && !allowAndDenyWereBothScanned)
                    {
                        skippedOppositeModeConflictPaths.Add(scannedGrant.Path);
                        continue;
                    }

                    grants.Add(new GrantedPathEntry
                    {
                        Path = scannedGrant.Path,
                        IsDeny = scannedGrant.IsDeny,
                        IsTraverseOnly = false,
                        SavedRights = scannedGrant.SavedRights
                    });
                    importedCount++;
                }
            }

            foreach (var traversePath in result.TraversePaths)
            {
                var normalizedPath = AclHelper.NormalizePath(traversePath);
                bool alreadyExists = grants.Any(e =>
                    string.Equals(AclHelper.NormalizePath(e.Path), normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                    e.IsTraverseOnly);

                if (alreadyExists)
                    continue;

                grants.Add(new GrantedPathEntry
                {
                    Path = normalizedPath,
                    IsTraverseOnly = true
                });
                importedCount++;
            }
        }

        var summary = new AclBulkScanImportSummary(
            importedCount,
            updatedCount,
            skippedOppositeModeConflictPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList());
        if (summary.HasChanges)
            saveDatabase();
        return summary;
    }

    private sealed record ScannedGrant(string Path, bool IsDeny, SavedRightsState SavedRights);
}
