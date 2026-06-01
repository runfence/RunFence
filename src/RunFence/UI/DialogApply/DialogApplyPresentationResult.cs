namespace RunFence.UI.DialogApply;

public sealed record DialogApplyPresentationResult(
    DialogApplyPresentationStatus Status,
    int ChangedCount = 0,
    bool RetainPendingInput = false,
    IReadOnlyList<string>? Warnings = null,
    IReadOnlyList<string>? Errors = null);
