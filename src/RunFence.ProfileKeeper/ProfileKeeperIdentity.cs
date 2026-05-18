namespace RunFence.ProfileKeeper;

public sealed record ProfileKeeperIdentity(int ProcessId, string Sid, string ExecutablePath);
