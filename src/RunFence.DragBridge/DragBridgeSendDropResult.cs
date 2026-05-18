namespace RunFence.DragBridge;

public sealed record DragBridgeSendDropResult(
    bool Succeeded,
    string? ErrorMessage);
