namespace RunFence.SidMigration;

/// <summary>
/// Removes a SID's or AppContainer's database references on account or container deletion.
/// Does NOT touch credentials or EphemeralAccounts — callers handle those.
/// </summary>
public interface ISidCleanupHelper
{
    /// <summary>
    /// Removes all database entries associated with <paramref name="sid"/>:
    /// apps owned by the SID (when <paramref name="removeApps"/> is true), global IPC callers,
    /// tray SIDs, per-app IPC caller / ACL references to the SID, account grants, and firewall settings.
    /// </summary>
    /// <returns>Counts of removed apps and removed global IPC callers.</returns>
    (int removedApps, int removedIpcCallers) CleanupSidFromAppData(
        string sid, bool removeApps = true);

    /// <summary>
    /// Removes all database entries associated with the given AppContainer name:
    /// the AppContainerEntry itself, and all AppEntry records referencing it.
    /// Also removes per-app IPC caller / ACL references to the container SID (if known).
    /// </summary>
    (int removedApps, int removedContainers) CleanupContainerFromAppData(
        string containerName, string? containerSid = null);
}