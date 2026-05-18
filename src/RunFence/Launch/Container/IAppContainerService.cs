using RunFence.Acl;
using RunFence.Core.Models;

namespace RunFence.Launch.Container;

/// <summary>
/// Composed interface for the full AppContainer service.
/// Use <see cref="IAppContainerProfileService"/> when only profile/identity operations are needed.
/// </summary>
public interface IAppContainerService : IAppContainerProfileService
{
    /// <summary>
    /// Enables or disables the loopback exemption for the named container.
    /// Returns true if the operation succeeded, false if it failed or was a no-op.
    /// </summary>
    Task<bool> SetLoopbackExemption(string name, bool enable);

    /// <summary>
    /// Grants the container SID COM launch and access permissions for the given AppID/CLSID.
    /// Modifies HKCR\AppID\{clsid} LaunchPermission and AccessPermission values.
    /// </summary>
    AppContainerComAccessResult GrantComAccess(string containerSid, string clsid);

    /// <summary>
    /// Revokes the container SID's COM launch and access permissions for the given AppID/CLSID.
    /// </summary>
    AppContainerComAccessResult RevokeComAccess(string containerSid, string clsid);

    /// <summary>
    /// Grants traverse access on ancestor directories so the AppContainer can reach
    /// <paramref name="path"/> and tracks them in the database.
    /// Returns whether the tracked traverse state changed, plus the full list of ancestor
    /// directories covered by the managed traverse entry.
    /// </summary>
    (bool Modified, List<string> AppliedPaths) EnsureTraverseAccess(AppContainerEntry entry, string path);

    /// <summary>
    /// Removes tracked grant and traverse ACEs for the container SID and returns any
    /// warning-grade persistence failures from completed cleanup.
    /// </summary>
    GrantApplyResult RevertTraverseAccess(AppContainerEntry entry, AppDatabase database);

}
