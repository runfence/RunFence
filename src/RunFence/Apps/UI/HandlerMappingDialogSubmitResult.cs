namespace RunFence.Apps.UI;

public sealed record HandlerMappingDialogSubmitResult(
    DialogResult? DialogResult,
    string? ValidationMessage,
    bool HasUnresolvedFailure,
    string? UnresolvedFailureText,
    string? WarningMessage,
    string? UnexpectedErrorMessage);
