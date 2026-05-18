namespace RunFence.Account;

public sealed record ProcessKillResult(
    int Killed,
    int Failed);
