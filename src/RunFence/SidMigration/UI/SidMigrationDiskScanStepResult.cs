namespace RunFence.SidMigration.UI;

public sealed record SidMigrationDiskScanStepResult(
    IReadOnlyCollection<string> OwnerDeleteBlockingSids,
    string CancelText);
