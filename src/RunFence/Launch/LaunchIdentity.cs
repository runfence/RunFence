namespace RunFence.Launch;

public abstract record LaunchIdentity
{
    public abstract string Sid { get; }
    public abstract bool? IsUnelevated { get; }
    public abstract TResult Visit<TResult>(ILaunchIdentityAcceptor<TResult> acceptor, ProcessLaunchTarget? target);
}
