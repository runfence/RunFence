using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Groups.UI.Forms;
using RunFence.Infrastructure;
using RunFence.UI.Forms;

namespace RunFence.Groups.UI;

/// <summary>
/// Orchestrates group actions: create/delete group, open ACL Manager, scan ACLs.
/// Does not hold references to grid controls — callers read grid state and pass it as parameters.
/// </summary>
public class GroupActionOrchestrator
{
    private readonly ILocalGroupMembershipService _groupMembership;
    private readonly GroupBulkScanOrchestrator? _bulkScanHandler;
    private readonly AccountAclManagerLauncher? _aclManagerLauncher;
    private readonly ISidNameCacheService _sidNameCache;
    private readonly ISessionSaver? _sessionSaver;
    private readonly ILoggingService _log;
    private readonly ISessionProvider _sessionProvider;

    public event Action? DataChanged;

    public GroupActionOrchestrator(
        ILocalGroupMembershipService groupMembership,
        GroupBulkScanOrchestrator? bulkScanHandler,
        AccountAclManagerLauncher? aclManagerLauncher,
        ISidNameCacheService sidNameCache,
        ISessionProvider sessionProvider,
        ILoggingService log,
        ISessionSaver? sessionSaver = null)
    {
        _groupMembership = groupMembership;
        _bulkScanHandler = bulkScanHandler;
        _aclManagerLauncher = aclManagerLauncher;
        _sidNameCache = sidNameCache;
        _sessionSaver = sessionSaver;
        _log = log;
        _sessionProvider = sessionProvider;
    }

    public bool IsAclManagerAvailable => _aclManagerLauncher != null;

    public bool IsBulkScanAvailable => _bulkScanHandler != null;

    public void CreateGroup(IWin32Window? owner)
    {
        using var dlg = new CreateGroupDialog(_groupMembership);
        if (DataPanel.ShowModal(dlg, owner) != DialogResult.OK)
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
            _groupMembership.DeleteGroup(sid);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to delete group {sid}", ex);
            MessageBox.Show($"Failed to delete group: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // OS deletion succeeded — clean up database entries
        GroupDatabaseHelper.CleanupDeletedGroupData(sid, _sessionProvider.GetSession().Database);
        _sessionSaver?.SaveConfig();
        DataChanged?.Invoke();
    }

    public void OpenAclManager(string sid, IWin32Window? owner)
    {
        if (_aclManagerLauncher == null)
            return;

        var displayName = _sidNameCache.GetDisplayName(sid);
        _aclManagerLauncher.OpenAclManager(sid, displayName, owner);
    }

    public async Task ScanAcls(IWin32Window owner, Action<bool> setScanButtonEnabled, Action<string> setStatusText)
    {
        if (_bulkScanHandler == null)
            return;

        await _bulkScanHandler.ScanAcls(
            owner,
            setScanButtonEnabled,
            setStatusText,
            () => _sessionSaver?.SaveConfig());
    }
}