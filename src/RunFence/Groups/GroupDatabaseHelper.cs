using RunFence.Core.Models;

namespace RunFence.Groups;

public static class GroupDatabaseHelper
{
    /// <summary>
    /// Removes all database entries for a group that has been deleted from the OS:
    /// cleans the group SID from all account group snapshots, clears grants for
    /// the group account entry, and removes the account entry if it becomes empty.
    /// Note: SidNames is append-only — the name entry is kept for SID migration.
    /// </summary>
    public static void CleanupDeletedGroupData(string sid, AppDatabase database)
    {
        if (database.AccountGroupSnapshots != null)
        {
            foreach (var groups in database.AccountGroupSnapshots.Values)
                groups.RemoveAll(g => string.Equals(g, sid, StringComparison.OrdinalIgnoreCase));
        }

        var groupAccount = database.GetAccount(sid);
        if (groupAccount != null)
        {
            groupAccount.Grants.Clear();
            database.RemoveAccountIfEmpty(sid);
        }
    }
}