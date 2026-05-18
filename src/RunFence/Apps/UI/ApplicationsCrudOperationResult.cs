namespace RunFence.Apps.UI;

public sealed record ApplicationsCrudOperationResult(
    ApplicationsCrudOperationStatus Status,
    string? ErrorMessage = null,
    string? WarningMessage = null);
