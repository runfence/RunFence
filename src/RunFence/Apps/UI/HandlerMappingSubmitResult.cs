namespace RunFence.Apps.UI;

public sealed record HandlerMappingSubmitResult(
    bool ShouldClose,
    bool SavedDurably,
    string? SaveError = null,
    string? RegistrySyncWarning = null);
