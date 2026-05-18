namespace RunFence.Account;

public sealed class AccountRestrictionOperationException : Exception
{
    public AccountRestrictionOperationException(
        string message,
        AccountRestrictionStatus status,
        bool rollbackAttempted,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Status = status;
        RollbackAttempted = rollbackAttempted;
    }

    public AccountRestrictionStatus Status { get; }

    public bool RollbackAttempted { get; }
}
