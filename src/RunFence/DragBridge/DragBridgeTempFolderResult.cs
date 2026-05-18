namespace RunFence.DragBridge;

public sealed record DragBridgeTempFolderResult(
    bool Succeeded,
    string? TempFolderPath,
    string? ErrorMessage);
