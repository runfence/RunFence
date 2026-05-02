using RunFence.Core.Models;

namespace RunFence.Launch.Container;

/// <summary>
/// Manages the per-container data folder lifecycle: creation, ACL management,
/// and access grants for the interactive user.
/// </summary>
public interface IAppContainerDataFolderService
{
    void EnsureContainerDataFolder(AppContainerEntry entry, string containerSid);
    void EnsureDataFolderTraverse(AppContainerEntry entry, string containerSid);
    void EnsureInteractiveUserAccess(AppContainerEntry entry);
}
