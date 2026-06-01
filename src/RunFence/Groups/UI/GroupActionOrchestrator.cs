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
    IGroupDeletePrompt deletePrompt,
    ISidNameCacheService sidNameCache,
    ILoggingService log)
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
        if (!deletePrompt.ConfirmDelete(name))
            return;

        var result = groupDeletionService.DeleteGroup(sid);
        if (result.Status == GroupDeletionStatus.OsDeleteFailed)
        {
            deletePrompt.ShowDeleteFailed(result.Errors.FirstOrDefault() ?? "Failed to delete group.");
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
            deletePrompt.ShowSaveFailed(displayName, result.SaveErrorMessage ?? string.Empty);
        }
    }

    public void OpenAclManager(string sid, IWin32Window? owner)
    {
        if (aclManagerLauncher == null)
            return;

        var displayName = sidNameCache.GetDisplayName(sid);
        aclManagerLauncher.OpenAclManager(sid, displayName, owner);
    }

    public async Task ScanAcls(IWin32Window owner, IGroupScanProgressPresenter progressPresenter)
    {
        if (bulkScanHandler == null)
            return;

        await bulkScanHandler.ScanAcls(
            owner,
            progressPresenter);
    }
}
