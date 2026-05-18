namespace RunFence.Persistence;

public sealed record MainConfigAdditionalAppIdRename(
    string ConfigPath,
    string OldAppId,
    string NewAppId);
