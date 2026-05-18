namespace RunFence.Launching.Processes;

public sealed record ProcessSnapshotInfo(
    int ProcessId,
    string Sid,
    string? ImagePath,
    long? CreationTimeUtcTicks);
