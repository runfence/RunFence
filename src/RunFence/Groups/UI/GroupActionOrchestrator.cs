using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Groups.UI.Forms;
using RunFence.Infrastructure;

namespace RunFence.Groups.UI;

public class GroupActionOrchestrator(
    IModalCoordinator modalCoordinator,
    ILocalGroupMutationService groupMembership,
    GroupDeletionService groupDeletionService,
    GroupBulkScanOrchestrator? bulkScanHandler,
    AccountAclManagerLauncher? aclManagerLauncher,
    ISidNameCacheService sidNameCache,
    ILoggingService log,
    ISessionSaver? sessionSaver = null)
{
    public event Action<string?>? DataChanged;

    public bool IsAclManagerAvailable => aclManagerLauncher != null;

    public bool IsBulkScanAvailable => bulkScanHandler != null;

    public void CreateGroup(IWin32Window? owner)
    {
        using var dlg = new CreateGroupDialog(groupMembership);
        if (modalCoordinator.ShowModal(dlg, owner) != DialogResult.OK)
            return;
        DataChanged?.Invoke(dlg.CreatedGroupSid);
    }

    public void DeleteGroup(string sid, string name)
    {
        var confirm = MessageBox.Show(
            $"Delete group '{name}'?\n\nThis will remove all ACL grants for this group.",
            "Confirm Delete Group", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
            return;

        var result = groupDeletionService.DeleteGroup(sid);
        if (result.Status == GroupDeletionStatus.OsDeleteFailed)
        {
            MessageBox.Show(
                result.Errors.FirstOrDefault() ?? "Failed to delete group.",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        foreach (var warning in result.Warnings)
            log.Warn(warning);
        foreach (var error in result.Errors)
            log.Error(error);

        if (result.DataChangedRaised)
            DataChanged?.Invoke(null);

        if (result.Status == GroupDeletionStatus.WindowsDeletedSaveFailed)
        {
            var displayName = result.GroupName ?? name;
            MessageBox.Show(
                $"Windows deleted group '{displayName}', but RunFence could not save the cleanup state:\n\n{result.SaveErrorMessage}",
                "Saved State Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
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
