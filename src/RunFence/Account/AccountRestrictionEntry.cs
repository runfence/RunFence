namespace RunFence.Account;

public sealed record AccountRestrictionEntry(
    AccountRestrictionKind Restriction,
    AccountRestrictionStatus Status,
    bool RollbackAttempted,
    string? Error);
