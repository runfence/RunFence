namespace RunFence.Account.Lifecycle;

public record AccountDeletionCleanupResult(IReadOnlyList<string> Warnings)
{
    public static AccountDeletionCleanupResult Success() => new([]);
}
