using RunFence.Core.Models;

namespace RunFence.Launch;

public class LaunchDefaultsResolver : ILaunchDefaultsResolver
{
    public LaunchIdentity ResolveDefaults(LaunchIdentity identity, AppDatabase databaseSnapshot)
        => identity.Visit(new SnapshotVisitor(databaseSnapshot), null);

    private sealed class SnapshotVisitor(AppDatabase databaseSnapshot) : ILaunchIdentityAcceptor<LaunchIdentity>
    {
        public LaunchIdentity Accept(AccountLaunchIdentity identity, ProcessLaunchTarget? target)
            => identity with
            {
                PrivilegeLevel = identity.PrivilegeLevel
                    ?? databaseSnapshot.GetAccount(identity.Sid)?.PrivilegeLevel
                    ?? PrivilegeLevel.Isolated
            };

        public LaunchIdentity Accept(AppContainerLaunchIdentity identity, ProcessLaunchTarget? target)
            => identity;
    }
}
