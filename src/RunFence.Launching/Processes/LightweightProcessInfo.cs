namespace RunFence.Launching.Processes;

public readonly record struct LightweightProcessInfo(
    int ProcessId,
    string ImageName,
    long? CreationTimeUtcTicks);
