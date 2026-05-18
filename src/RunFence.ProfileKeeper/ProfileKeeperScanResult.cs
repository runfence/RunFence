namespace RunFence.ProfileKeeper;

public sealed record ProfileKeeperScanResult(
    bool IsNewestKeeper,
    int SameSidBlockingProcessCount,
    IReadOnlyList<int> IgnorableProcessIds);
