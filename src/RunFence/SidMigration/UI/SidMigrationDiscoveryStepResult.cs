namespace RunFence.SidMigration.UI;

public sealed record SidMigrationDiscoveryStepResult(
    string CompletionText,
    string CancelText,
    string? UnresolvedWarningMessage);
