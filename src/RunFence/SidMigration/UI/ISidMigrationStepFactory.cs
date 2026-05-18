using RunFence.Core.Models;

namespace RunFence.SidMigration.UI;

public interface ISidMigrationStepFactory
{
    ISidMigrationPathStepView CreatePathStep(bool showSkipButton);

    ISidMigrationProgressStepView CreateDiscoveryProgressStep();

    ISidMigrationMappingStepView CreateMappingStep(IReadOnlyList<OrphanedSid> orphanedSids);

    ISidMigrationProgressStepView CreateDiskScanProgressStep();

    ISidMigrationStepView CreatePreviewStep(IReadOnlyList<SidMigrationMatch> scanResults);

    ISidMigrationDiskApplyStepView CreateDiskApplyProgressStep();

    ISidMigrationInAppStepView CreateInAppStep(
        IReadOnlyList<SidMigrationMapping> filteredMappings,
        IReadOnlyList<string> filteredDeletes,
        IEnumerable<string> unresolvedSids);
}
