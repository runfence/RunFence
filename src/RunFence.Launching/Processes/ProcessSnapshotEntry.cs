namespace RunFence.Launching.Processes;

public readonly record struct ProcessSnapshotEntry(
    int ProcessId,
    long? CreationTimeUtcTicks);
