using RunFence.Account;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.SidMigration.UI;

public sealed class SidMigrationWorkflowFactory(
    SessionContext session,
    ISidMigrationService sidMigrationService,
    IMessageBoxService messageBoxService,
    ILoggingService log,
    ILocalUserProvider localUserProvider,
    IProfilePathResolver profilePathResolver,
    ISidNameCacheService sidNameCache,
    Func<InAppMigrationHandler> createInAppMigrationHandler,
    Func<SidMigrationDiscoveryStepController> createDiscoveryStepController,
    Func<SidMigrationDiskScanStepController> createDiskScanStepController,
    Func<SidMigrationDiskApplyController> createDiskApplyController)
{
    public SidMigrationWorkflowController Create()
    {
        var diskApplyController = createDiskApplyController();
        var progressCoordinator = new SidMigrationProgressCoordinator(
            log,
            messageBoxService,
            diskApplyController);
        var stepFactory = new SidMigrationStepFactory(
            session,
            sidMigrationService,
            createInAppMigrationHandler(),
            localUserProvider,
            log,
            profilePathResolver,
            sidNameCache,
            messageBoxService);

        return new SidMigrationWorkflowController(
            session,
            sidMigrationService,
            messageBoxService,
            new SidMigrationWorkflowState(),
            progressCoordinator,
            stepFactory,
            createDiscoveryStepController(),
            createDiskScanStepController());
    }
}
