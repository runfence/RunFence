using RunFence.Account;

namespace RunFence.RunAs;

public sealed class RunAsCredentialPersistenceException : Exception
{
    public RunAsCredentialPersistenceException(
        CreatedAccountRollbackState rollbackState,
        Exception saveException)
        : base(saveException.Message, saveException)
    {
        RollbackState = rollbackState;
        SaveException = saveException;
    }

    public CreatedAccountRollbackState RollbackState { get; }

    public Exception SaveException { get; }
}
