using RunFence.Core.Ipc;

namespace RunFence.Launcher;

public sealed record LauncherCommandResult(
    LauncherCommandKind CommandKind,
    string? RawTail,
    IpcMessage? IpcMessage,
    IpcErrorCode? RecoverableIpcError,
    LauncherFallbackAction FallbackAction,
    string? Warning,
    int ExitCode);
