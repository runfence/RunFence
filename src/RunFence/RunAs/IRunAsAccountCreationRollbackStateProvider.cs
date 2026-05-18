using RunFence.Account;

namespace RunFence.RunAs;

public interface IRunAsAccountCreationRollbackStateProvider
{
    CreatedAccountRollbackState? CreatedRollbackState { get; }
}
