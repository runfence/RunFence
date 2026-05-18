namespace RunFence.Account.UI;

public enum AccountListRefreshStatus
{
    Applied,
    StaleIgnored,
    Canceled,
    Failed
}

public sealed record AccountListRefreshResult(
    long GenerationId,
    AccountListRefreshStatus Status,
    IReadOnlyList<IAccountGridRow>? Rows = null,
    string? Error = null);

public sealed class AccountListPresenter
{
    private long _generation;

    public long NextGeneration() => Interlocked.Increment(ref _generation);

    public bool IsCurrent(long generation) => generation == Volatile.Read(ref _generation);

    public AccountListRefreshResult Applied(long generation, IReadOnlyList<IAccountGridRow>? rows = null)
        => new(generation, AccountListRefreshStatus.Applied, rows);

    public AccountListRefreshResult StaleIgnored(long generation)
        => new(generation, AccountListRefreshStatus.StaleIgnored);

    public AccountListRefreshResult Canceled(long generation)
        => new(generation, AccountListRefreshStatus.Canceled);

    public AccountListRefreshResult Failed(long generation, string error)
        => new(generation, AccountListRefreshStatus.Failed, Error: error);
}
