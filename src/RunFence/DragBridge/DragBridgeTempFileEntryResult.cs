namespace RunFence.DragBridge;

public sealed record DragBridgeTempFileEntryResult(
    string SourcePath,
    string? TempPath,
    DragBridgeTempFileCopyStatus CopyStatus,
    DragBridgeTempFileGrantStatus GrantStatus,
    DragBridgeTempFileRollbackStatus RollbackStatus,
    string? ErrorText);
