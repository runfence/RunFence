namespace RunFence.Account;

public sealed record AccountPasswordResult(AccountPasswordStatus Status, string Sid, string? Error);
