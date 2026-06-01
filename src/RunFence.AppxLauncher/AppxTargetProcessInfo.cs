namespace RunFence.AppxLauncher;

public readonly record struct AppxTargetProcessInfo(
    int ProcessId,
    DateTime? StartTimeUtc,
    string ExecutablePath);
