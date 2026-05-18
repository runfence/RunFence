namespace RunFence.Core;

public static class IpcConstants
{
    public static readonly string PipeName = "RunFence" + DebugHelper.AppIdSuffix;
    public const int MaxPipeMessageSize = 64 * 1024; // 64 KB

    public const int LauncherTimeoutMs = 30_000; // 30 seconds
    public const int PipeConnectTimeoutMs = 5_000;
    public const int JobKeeperLaunchIpcTimeoutMs = 5_000;

    public const string MutexName = @"Global\RunFence";

    public const int DragBridgePipeConnectTimeoutMs = 10_000;
}
