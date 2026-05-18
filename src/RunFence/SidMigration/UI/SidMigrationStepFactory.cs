using RunFence.Account;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.SidMigration.UI.Forms;

namespace RunFence.SidMigration.UI;

public sealed class SidMigrationStepFactory(
    SessionContext session,
    ISidMigrationService sidMigrationService,
    InAppMigrationHandler inAppMigrationHandler,
    ILocalUserProvider localUserProvider,
    ILoggingService log,
    IProfilePathResolver profilePathResolver,
    ISidNameCacheService sidNameCache,
    IMessageBoxService messageBoxService) : ISidMigrationStepFactory
{
    public ISidMigrationPathStepView CreatePathStep(bool showSkipButton)
    {
        return new SidMigrationPathStep(showSkipButton)
        {
            Dock = DockStyle.Fill
        };
    }

    public ISidMigrationProgressStepView CreateDiscoveryProgressStep()
    {
        return new SidMigrationDiscoveryProgressStep
        {
            Dock = DockStyle.Top
        };
    }

    public ISidMigrationMappingStepView CreateMappingStep(IReadOnlyList<OrphanedSid> orphanedSids)
    {
        return new MigrationMappingStep(
            session,
            sidMigrationService,
            localUserProvider,
            log,
            orphanedSids,
            profilePathResolver,
            sidNameCache,
            messageBoxService)
        {
            Dock = DockStyle.Fill
        };
    }

    public ISidMigrationProgressStepView CreateDiskScanProgressStep()
    {
        return new SidMigrationDiskScanProgressStep
        {
            Dock = DockStyle.Top
        };
    }

    public ISidMigrationStepView CreatePreviewStep(IReadOnlyList<SidMigrationMatch> scanResults)
    {
        return new SidMigrationPreviewStep(scanResults)
        {
            Dock = DockStyle.Fill
        };
    }

    public ISidMigrationDiskApplyStepView CreateDiskApplyProgressStep()
    {
        return new SidMigrationDiskApplyStepView
        {
            Dock = DockStyle.Fill
        };
    }

    public ISidMigrationInAppStepView CreateInAppStep(
        IReadOnlyList<SidMigrationMapping> filteredMappings,
        IReadOnlyList<string> filteredDeletes,
        IEnumerable<string> unresolvedSids)
    {
        return new SidMigrationInAppStep(
            inAppMigrationHandler,
            session,
            filteredMappings.ToList(),
            filteredDeletes.ToList(),
            profilePathResolver,
            unresolvedSids)
        {
            Dock = DockStyle.Fill
        };
    }
}
