using RunFence.Core.Models;

namespace RunFence.SidMigration.UI;

public class SidMigrationDiskScanStepController
{
    public SidMigrationDiskScanStepResult BuildResult(
        IReadOnlyList<SidMigrationMatch> scanResults,
        IReadOnlyCollection<string> selectedDeleteSids)
    {
        var ownerDeleteBlockingSids = scanResults
            .Where(hit => hit.MatchType.HasFlag(SidMigrationMatchType.Owner)
                          && !string.IsNullOrEmpty(hit.OwnerOldSid)
                          && selectedDeleteSids.Contains(hit.OwnerOldSid, StringComparer.OrdinalIgnoreCase))
            .Select(hit => hit.OwnerOldSid!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var cancelText = scanResults.Count > 0
            ? $"Scan cancelled. Found {scanResults.Count:N0} items so far."
            : "Scan cancelled.";

        return new SidMigrationDiskScanStepResult(ownerDeleteBlockingSids, cancelText);
    }
}

