using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Groups.UI.Forms;
using RunFence.Infrastructure;

namespace RunFence.Groups.UI;

/// <summary>
/// Orchestrates group actions: create/delete group, open ACL Manager, scan ACLs.
/// Does not hold references to grid controls — callers read grid state and pass it as parameters.
/// </summary>
/// <remarks>
/// <paramref name="bulkScanHandler"/> and <paramref name="aclManagerLauncher"/> are nullable
/// because they are unavailable in the foundation scope (pre-session). Available after BeginSessionScope().
/// <paramref name="sessionSaver"/> is nullable for the same reason.
/// </remarks>
public class GroupActionOrchestrator(
    IModalCoordinator modalCoordinator,
    ILocalGroupMembershipService groupMembership,
    GroupBulkScanOrchestrator? bulkScanHandler,
    AccountAclManagerLauncher? aclManagerLauncher,
    ISidNameCacheService sidNameCache,
    ISessionProvider sessionProvider,
    ILoggingService log,
    ISessionSaver? sessionSaver = null)
{
    public event Action? DataChanged;

    public bool IsAclManagerAvailable => aclManagerLauncher != null;

    public bool IsBulkScanAvailable => bulkScanHandler != null;

    public void CreateGroup(IWin32Window? owner)
    {
        using var dlg = new CreateGroupDialog(groupMembership);
        if (modalCoordinator.ShowModal(dlg, owner) != DialogResult.OK)
            return;
        DataChanged?.Invoke();
    }

    public void DeleteGroup(string sid, string name)
    {
        var confirm = MessageBox.Show(
            $"Delete group '{name}'?\n\nThis will remove all ACL grants for this group.",
            "Confirm Delete Group", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
            return;

        try
        {
            groupMembership.DeleteGroup(sid);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to delete group {sid}", ex);
            MessageBox.Show($"Failed to delete group: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // OS deletion succeeded — clean up database entries
        GroupDatabaseHelper.CleanupDeletedGroupData(sid, sessionProvider.GetSession().Database);
        sessionSaver?.SaveConfig();
        DataChanged?.Invoke();
    }

    public void OpenAclManager(string sid, IWin32Window? owner)
    {
        if (aclManagerLauncher == null)
            return;

        var displayName = sidNameCache.GetDisplayName(sid);
        aclManagerLauncher.OpenAclManager(sid, displayName, owner);
    }

    public async Task ScanAcls(IWin32Window owner, Action<bool> setScanButtonEnabled, Action<string> setStatusText)
    {
        if (bulkScanHandler == null)
            return;

        await bulkScanHandler.ScanAcls(
            owner,
            setScanButtonEnabled,
            setStatusText,
            () => sessionSaver?.SaveConfig());
    }
}