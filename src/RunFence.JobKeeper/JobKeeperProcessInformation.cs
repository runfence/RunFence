namespace RunFence.JobKeeper;

public readonly record struct JobKeeperProcessInformation(
    IntPtr ProcessHandle,
    IntPtr ThreadHandle,
    uint ProcessId,
    uint ThreadId);
