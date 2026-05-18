namespace RunFence.Apps.UI;

public sealed record AppEditSaveResult(
    AppEditSaveStatus Status,
    string? SaveError = null,
    string? RegistrySyncWarning = null);
