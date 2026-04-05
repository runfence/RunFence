using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.UI;

/// <summary>
/// Shared constants and filtering logic for local group lists shown in account dialogs.
/// </summary>
public static class GroupFilterHelper
{
    public const string UsersSid = "S-1-5-32-545";
    public const string AdministratorsSid = "S-1-5-32-544";

    /// <summary>
    /// Well-known service/system group SIDs that are irrelevant for user creation/editing.
    /// </summary>
    private static readonly HashSet<string> FilteredGroupSids = new(StringComparer.OrdinalIgnoreCase)
    {
        "S-1-5-32-558", // Performance Monitor Users
        "S-1-5-32-559", // Performance Log Users
        "S-1-5-32-562", // Distributed COM Users
        "S-1-5-32-568", // IIS_IUSRS
        "S-1-5-32-573", // Event Log Readers
        "S-1-5-32-574", // Certificate Service DCOM Access
        "S-1-5-32-579", // Access Control Assistance Operators
        "S-1-5-32-580", // Remote Management Users
    };

    private static readonly HashSet<string> FilteredGroupNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "OpenSSH Users",
        "System Managed Accounts Group",
        "Device Owners",
    };

    /// <summary>
    /// Returns true if the account with the given SID is a member of the Administrators group.
    /// Returns false on failure (e.g., unresolvable SID).
    /// </summary>
    public static bool IsAdminAccount(string sid, ILocalGroupMembershipService groupMembership)
    {
        try
        {
            return groupMembership.GetGroupsForUser(sid)
                .Any(g => string.Equals(g.Sid, AdministratorsSid, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns an ordering key for a group SID: Users first (0), Administrators second (1), rest last (2).
    /// </summary>
    private static int GroupSortOrder(string sid) =>
        string.Equals(sid, UsersSid, StringComparison.OrdinalIgnoreCase) ? 0 :
        string.Equals(sid, AdministratorsSid, StringComparison.OrdinalIgnoreCase) ? 1 : 2;

    /// <summary>
    /// Filters and sorts groups for use in the Create User dialog.
    /// Excludes well-known service/system groups not relevant to user accounts.
    /// </summary>
    public static IEnumerable<LocalUserAccount> FilterForCreateDialog(IEnumerable<LocalUserAccount> groups) =>
        groups
            .Where(g => !FilteredGroupSids.Contains(g.Sid) && !FilteredGroupNames.Contains(g.Username))
            .OrderBy(g => GroupSortOrder(g.Sid))
            .ThenBy(g => g.Username, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Filters and sorts groups for the Groups tab management panel.
    /// Shows all groups except OS-internal name-filtered ones (OpenSSH Users, System Managed Accounts Group, Device Owners).
    /// Does NOT apply FilteredGroupSids — those are only for account dialogs.
    /// </summary>
    public static IEnumerable<LocalUserAccount> FilterForGroupsPanel(IEnumerable<LocalUserAccount> groups) =>
        groups
            .Where(g => !FilteredGroupNames.Contains(g.Username))
            .OrderBy(g => GroupSortOrder(g.Sid))
            .ThenBy(g => g.Username, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Filters and sorts groups for use in the Edit Account dialog.
    /// Always includes groups the account is already a member of, and groups in <paramref name="neverFilteredGroupNames"/>.
    /// </summary>
    public static IEnumerable<LocalUserAccount> FilterForEditDialog(
        IEnumerable<LocalUserAccount> groups,
        ISet<string> currentGroupSids,
        ISet<string> neverFilteredGroupNames) =>
        groups
            .Where(g => currentGroupSids.Contains(g.Sid)
                        || neverFilteredGroupNames.Contains(g.Username)
                        || (!FilteredGroupSids.Contains(g.Sid) && !FilteredGroupNames.Contains(g.Username)))
            .OrderBy(g => GroupSortOrder(g.Sid))
            .ThenBy(g => g.Username, StringComparer.OrdinalIgnoreCase);
}