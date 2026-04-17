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
    bool SetLoopbackExemption(string name, bool enable);

    /// <summary>
    /// Grants the container SID COM launch and access permissions for the given AppID/CLSID.
    /// Modifies HKCR\AppID\{clsid} LaunchPermission and AccessPermission values.
    /// </summary>
    void GrantComAccess(string containerSid, string clsid);

    /// <summary>
    /// Revokes the container SID's COM launch and access permissions for the given AppID/CLSID.
    /// </summary>
    void RevokeComAccess(string containerSid, string clsid);

    /// <summary>
    /// Grants traverse access on ancestor directories so the AppContainer can reach
    /// <paramref name="path"/> and tracks them in the database.
    /// Returns <c>Modified = true</c> if a new path was added (caller should save config),
    /// and <c>AppliedPaths</c> = the full list of ancestor directories visited (for
    /// <see cref="GrantedPathEntry.AllAppliedPaths"/>).
    /// </summary>
    (bool Modified, List<string> AppliedPaths) EnsureTraverseAccess(AppContainerEntry entry, string path);

    /// <summary>
    /// Removes traverse ACEs for the container SID from all directories tracked in
    /// <c>database.AccountGrants[containerSid]</c> plus the container data folder.
    /// Call before DeleteProfile to clean up lingering ACEs.
    /// </summary>
    void RevertTraverseAccess(AppContainerEntry entry, AppDatabase database);

    /// <summary>
    /// Reverts traverse ACEs for the given entry, preserving those still needed by remaining paths.
    /// Uses <see cref="GrantedPathEntry.AllAppliedPaths"/> when available for reliable cleanup.
    /// </summary>
    void RevertTraverseAccessForPath(AppContainerEntry entry, GrantedPathEntry grantedEntry, AppDatabase database);
}
