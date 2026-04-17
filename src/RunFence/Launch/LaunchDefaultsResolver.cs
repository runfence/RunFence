using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Launch;

public class LaunchDefaultsResolver(ISessionProvider sessionProvider) : ILaunchDefaultsResolver, ILaunchIdentityAcceptor<LaunchIdentity>
{
    public LaunchIdentity ResolveDefaults(LaunchIdentity identity)
        => identity.Visit(this, null);

    public LaunchIdentity Accept(AccountLaunchIdentity identity, ProcessLaunchTarget? target)
        => identity with { PrivilegeLevel = identity.PrivilegeLevel ?? GetAccountDefault(identity.Sid) };

    public LaunchIdentity Accept(AppContainerLaunchIdentity identity, ProcessLaunchTarget? target)
        => identity;

    private PrivilegeLevel GetAccountDefault(string sid)
        => sessionProvider.GetSession().Database.GetAccount(sid)?.PrivilegeLevel ?? PrivilegeLevel.Basic;
}
