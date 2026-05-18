namespace RunFence.Account;

public sealed record AccountRestrictionResult(IReadOnlyList<AccountRestrictionEntry> Entries);
