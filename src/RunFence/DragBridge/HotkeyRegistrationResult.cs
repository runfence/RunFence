namespace RunFence.DragBridge;

public sealed record HotkeyRegistrationResult(
    HotkeyRegistrationStatus Status,
    int HotkeyId,
    int Modifiers,
    int Key,
    string? Error);
