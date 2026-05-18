namespace RunFence.Account.UI;

public sealed record AccountDeletionPreflightRequest(
    string Sid,
    string DisplayName,
    bool IsUnavailable,
    bool IsSystemSid);
