using RunFence.Core.Helpers;
using RunFence.Persistence;

namespace RunFence.SidMigration;

/// <summary>
/// Shared helper for removing a SID's or AppContainer's database references.
/// Cleans apps, IPC callers, tray SIDs, and per-app references.
/// Does NOT touch credentials — callers handle those.
/// </summary>
public class SidCleanupHelper(
    IDatabaseProvider databaseProvider,
    ITrackingJobStateStore? trackingJobStateStore = null) : ISidCleanupHelper
{
    public (int removedApps, int removedIpcCallers) CleanupSidFromAppData(
        string sid, bool removeApps = true)
    {
        var database = databaseProvider.GetDatabase();
        int removedApps = removeApps
            ? database.Apps.RemoveAll(a =>
                SidComparer.SidEquals(a.AccountSid, sid))
            : 0;

        // Remove the AccountEntry for this SID (covers IsIpcCaller, tray flags, privilege level, etc.)
        var entry = database.GetAccount(sid);
        int removedCallers = 0;
        if (entry != null)
        {
            if (entry.IsIpcCaller)
                removedCallers = 1;
            database.Accounts.Remove(entry);
        }

        foreach (var app in database.Apps)
        {
            app.AllowedIpcCallers?.RemoveAll(s =>
                SidComparer.SidEquals(s, sid));
            app.AllowedAclEntries?.RemoveAll(e =>
                SidComparer.SidEquals(e.Sid, sid));
        }

        database.AccountGroupSnapshots?.Remove(sid);
        trackingJobStateStore?.RemoveTrackingJobSid(sid, saveImmediately: false);

        return (removedApps, removedCallers);
    }

    public (int removedApps, int removedContainers) CleanupContainerFromAppData(
        string containerName, string? containerSid = null)
    {
        var database = databaseProvider.GetDatabase();
        int removedApps = database.Apps.RemoveAll(a =>
            string.Equals(a.AppContainerName, containerName, StringComparison.OrdinalIgnoreCase));
        int removedContainers = database.AppContainers.RemoveAll(c =>
            string.Equals(c.Name, containerName, StringComparison.OrdinalIgnoreCase));

        if (containerSid != null)
        {
            foreach (var app in database.Apps)
            {
                app.AllowedIpcCallers?.RemoveAll(s =>
                    SidComparer.SidEquals(s, containerSid));
                app.AllowedAclEntries?.RemoveAll(e =>
                    SidComparer.SidEquals(e.Sid, containerSid));
            }

            var containerEntry = database.GetAccount(containerSid);
            if (containerEntry != null)
                database.Accounts.Remove(containerEntry);

            trackingJobStateStore?.RemoveTrackingJobSid(containerSid, saveImmediately: false);
        }

        return (removedApps, removedContainers);
    }
}
