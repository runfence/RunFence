using System.Security.AccessControl;
using RunFence.Acl;

namespace RunFence.Launch;

public interface ILaunchAccessManager
{
    /// <summary>
    /// Grants <paramref name="rights"/> to <paramref name="identity"/> on <paramref name="path"/>.
    /// Uses <see cref="LaunchIdentity.IsUnelevated"/> to decide whether Administrators rights count.
    /// For Low Integrity accounts, also grants to S-1-16-4096 using the same rights.
    /// </summary>
    GrantApplyResult EnsureAccess(LaunchIdentity identity, string path, FileSystemRights rights,
        Func<string, string, bool>? confirm);

    GrantApplyResult EnsureAccess(string sid, string path, FileSystemRights rights,
        Func<string, string, bool>? confirm, bool unelevated);
}
