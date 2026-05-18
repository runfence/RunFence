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
    public async Task<ContainerDeletionResult> DeleteContainer(AppContainerEntry entry, string? containerSid)
    {
        var database = databaseProvider.GetDatabase();
        try
        {
            await appContainerService.DeleteProfile(entry.Name, entry.EnableLoopback);
        }
        catch (Exception ex)
        {
            log.Warn($"ContainerDeletionService: DeleteProfile failed for '{entry.Name}': {ex.Message}");
            return ContainerDeletionResult.Failure(ex.Message);
        }

        GrantApplyResult revertResult;
        try
        {
            // Reverts all grants and traverse for the container SID (including interactive user
            // grant cleanup with SavedRights equality check and other-container dependency checks).
            revertResult = appContainerService.RevertTraverseAccess(entry, database);
        }
        catch (Exception ex)
        {
            log.Warn($"ContainerDeletionService: RevertTraverseAccess failed for '{entry.Name}': {ex.Message}");
            return ContainerDeletionResult.Failure(ex.Message);
        }

        if (!string.IsNullOrEmpty(containerSid))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            environmentSetup.TryRevokeVirtualStoreAccess(containerSid, localAppData);
        }

        sidCleanup.CleanupContainerFromAppData(entry.Name, containerSid);

        if (revertResult.Warnings.Count == 0)
            return ContainerDeletionResult.Success();

        var warnings = new List<string>(revertResult.Warnings.Count);
        foreach (var warning in revertResult.Warnings)
            warnings.Add(GrantApplyFailureFormatter.Format(warning));

        return ContainerDeletionResult.Success(warnings);
    }
}
