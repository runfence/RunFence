namespace RunFence.PrefTrans;

public record SettingsTransferResult(bool Success, string Message, bool DatabaseModified = false);