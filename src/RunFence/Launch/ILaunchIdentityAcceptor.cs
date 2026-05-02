namespace RunFence.Launch;

public interface ILaunchIdentityAcceptor<out TResult>
{
    TResult Accept(AccountLaunchIdentity identity, ProcessLaunchTarget? target);
    TResult Accept(AppContainerLaunchIdentity identity, ProcessLaunchTarget? target);
}
