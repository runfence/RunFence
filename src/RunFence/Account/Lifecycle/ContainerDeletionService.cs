using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Container;
using RunFence.Persistence;
using RunFence.SidMigration;

namespace RunFence.Account.Lifecycle;

public class ContainerDeletionService(
    IAppContainerService appContainerService,
    IGrantedPathAclService grantedPathAcl,
    ISidCleanupHelper sidCleanup,
    ILoggingService log,
    IAppContainerEnvironmentSetup environmentSetup,
    IInteractiveUserResolver interactiveUserResolver,
    IDatabaseProvider databaseProvider) : IContainerDeletionService
{
    public bool DeleteContainer(AppContainerEntry entry, string? containerSid)
    {
        var database = databaseProvider.GetDatabase();
        try
        {
            appContainerService.RevertTraverseAccess(entry, database);
        }
        catch (Exception ex)
        {
            log.Warn($"ContainerDeletionService: RevertTraverseAccess failed for '{entry.Name}': {ex.Message}");
        }

        try
        {
            appContainerService.DeleteProfile(entry.Name, entry.EnableLoopback);
        }
        catch (Exception ex)
        {
            log.Warn($"ContainerDeletionService: DeleteProfile failed for '{entry.Name}': {ex.Message}");
            return false;
        }

        if (!string.IsNullOrEmpty(containerSid))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            environmentSetup.TryRevokeVirtualStoreAccess(containerSid, localAppData);

            var grants = database.GetAccount(containerSid)?.Grants;
            if (grants is { Count: > 0 })
            {
                try
                {
                    grantedPathAcl.RevertAllGrantsBatch(grants, containerSid);
                }
                catch (Exception ex)
                {
                    log.Warn($"ContainerDeletionService: RevertAllGrantsBatch failed for '{entry.Name}': {ex.Message}");
                }

                // Sync: also revert the interactive user's grants for the same paths.
                RevertInteractiveUserGrants(grants, database, entry.Name);
            }
        }

        sidCleanup.CleanupContainerFromAppData(entry.Name, containerSid);
        return true;
    }

    private void RevertInteractiveUserGrants(
        IReadOnlyList<GrantedPathEntry> containerGrants, AppDatabase database, string containerName)
    {
        var iuSid = interactiveUserResolver.GetInteractiveUserSid();
        if (string.IsNullOrEmpty(iuSid))
            return;
        var iuAccount = database.GetAccount(iuSid);
        if (iuAccount == null)
            return;
        var iuEntries = iuAccount.Grants;

        // Only consider allow-mode paths — deny grants are never synced to the interactive user.
        // Key: path → SavedRights (used to match against the IU's grant for equality check below).
        var containerAllowGrants = containerGrants
            .Where(g => g is { IsTraverseOnly: false, IsDeny: false })
            .ToDictionary(g => g.Path, g => g.SavedRights, StringComparer.OrdinalIgnoreCase);

        // Collect paths granted by other containers so we don't remove the IU grant
        // for a path that another container also needs.
        var otherContainerPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var other in database.AppContainers)
        {
            if (string.Equals(other.Name, containerName, StringComparison.OrdinalIgnoreCase))
                continue;
            string otherSid;
            try
            {
                otherSid = appContainerService.GetSid(other.Name);
            }
            catch
            {
                continue;
            }

            var otherGrants = database.GetAccount(otherSid)?.Grants;
            if (otherGrants == null)
                continue;
            foreach (var g in otherGrants.Where(g => g is { IsTraverseOnly: false, IsDeny: false }))
                otherContainerPaths.Add(g.Path);
        }

        // Only revert IU grants whose SavedRights exactly match the container's — if they differ,
        // the IU has rights from another source that must not be removed.
        // Also skip paths that are still needed by another container.
        var toRevert = iuEntries
            .Where(g => g is { IsTraverseOnly: false, IsDeny: false } &&
                        containerAllowGrants.TryGetValue(g.Path, out var containerRights) &&
                        g.SavedRights == containerRights &&
                        !otherContainerPaths.Contains(g.Path))
            .ToList();

        if (toRevert.Count == 0)
            return;

        try
        {
            grantedPathAcl.RevertAllGrantsBatch(toRevert, iuSid);
        }
        catch (Exception ex)
        {
            log.Warn($"ContainerDeletionService: failed to revert interactive user grants for '{containerName}': {ex.Message}");
        }

        // Remove from DB — only the matched entries (those with equal SavedRights).
        var revertedPaths = toRevert.Select(g => g.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        iuEntries.RemoveAll(g => g is { IsTraverseOnly: false, IsDeny: false } && revertedPaths.Contains(g.Path));
        database.RemoveAccountIfEmpty(iuSid);
    }
}