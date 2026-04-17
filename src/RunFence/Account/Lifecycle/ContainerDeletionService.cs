using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Container;
using RunFence.Persistence;
using RunFence.SidMigration;

namespace RunFence.Account.Lifecycle;

public class ContainerDeletionService(
    IAppContainerService appContainerService,
    ISidCleanupHelper sidCleanup,
    ILoggingService log,
    IAppContainerEnvironmentSetup environmentSetup,
    IDatabaseProvider databaseProvider) : IContainerDeletionService
{
    public bool DeleteContainer(AppContainerEntry entry, string? containerSid)
    {
        var database = databaseProvider.GetDatabase();
        try
        {
            // Reverts all grants and traverse for the container SID (including interactive user
            // grant cleanup with SavedRights equality check and other-container dependency checks).
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
        }

        sidCleanup.CleanupContainerFromAppData(entry.Name, containerSid);

        return true;
    }
}