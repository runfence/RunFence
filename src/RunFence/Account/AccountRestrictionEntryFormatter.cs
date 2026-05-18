namespace RunFence.Account;

public static class AccountRestrictionEntryFormatter
{
    public static string Format(AccountRestrictionEntry entry)
        => $"{entry.Restriction}: {entry.Error ?? entry.Status.ToString()}";
}
