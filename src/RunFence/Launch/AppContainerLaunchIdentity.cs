using RunFence.Core.Models;
using RunFence.Launch.Container;

namespace RunFence.Launch;

public record AppContainerLaunchIdentity(AppContainerEntry Entry) : LaunchIdentity
{
    public override string Sid => Entry.Sid;
    public override bool? IsUnelevated => true;

    public override TResult Visit<TResult>(ILaunchIdentityAcceptor<TResult> acceptor, ProcessLaunchTarget? target)
        => acceptor.Accept(this, target);
}
