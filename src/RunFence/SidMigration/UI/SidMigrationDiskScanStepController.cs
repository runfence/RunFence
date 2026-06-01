using RunFence.Core.Models;

namespace RunFence.SidMigration.UI;

public class SidMigrationDiskScanStepController
{
    public SidMigrationDiskScanStepResult BuildResult(
        IReadOnlyList<SidMigrationMatch> scanResults)
    {
        var cancelText = scanResults.Count > 0
            ? $"Scan cancelled. Found {scanResults.Count:N0} items so far."
            : "Scan cancelled.";

        return new SidMigrationDiskScanStepResult(cancelText);
    }
}

