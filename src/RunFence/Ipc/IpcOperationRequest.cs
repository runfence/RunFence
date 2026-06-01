namespace RunFence.Ipc;

public sealed record IpcOperationRequest(
    string? CallerIdentity,
    string? CallerSid,
    bool IsAdmin);
