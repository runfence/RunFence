namespace RunFence.PrefTrans;

public record SettingsTransferGrantResult(
    bool Succeeded,
    bool GrantCreated,
    string? WarningMessage);
