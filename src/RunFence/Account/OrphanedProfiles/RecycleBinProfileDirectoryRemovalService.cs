using RunFence.Infrastructure;

namespace RunFence.Account.OrphanedProfiles;

/// <summary>
/// Removes a previously renamed orphaned profile directory by sending it to the Windows Recycle Bin.
/// The caller is responsible for the safety checks and rename-to-`.deleted` step before invoking this service.
/// </summary>
public class RecycleBinProfileDirectoryRemovalService : IProfileDirectoryRemovalService
{
    public void RemoveMovedProfileDirectory(string path)
        => ShellNative.MoveToRecycleBin(path);
}
