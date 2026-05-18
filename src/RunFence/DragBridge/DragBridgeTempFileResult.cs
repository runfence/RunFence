namespace RunFence.DragBridge;

public sealed record DragBridgeTempFileResult(
    bool Succeeded,
    IReadOnlyList<DragBridgeTempFileEntryResult> Entries,
    IReadOnlyList<string> TempPaths);
