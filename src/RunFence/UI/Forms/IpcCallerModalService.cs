using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.RunAs.UI.Forms;

namespace RunFence.UI.Forms;

public sealed class IpcCallerModalService(IModalCoordinator modalCoordinator) : IIpcCallerModalService
{
    public CallerIdentitySelectionResult PromptForCallerIdentity(IWin32Window? owner, IReadOnlyList<LocalUserAccount> localUsers, ISidEntryHelper sidEntryHelper)
    {
        using var dlg = new CallerIdentityDialog(localUsers, sidEntryHelper);
        var result = modalCoordinator.ShowModal(dlg, owner);
        return result == DialogResult.OK && dlg.Result != null
            ? new CallerIdentitySelectionResult(true, dlg.Result, dlg.ResolvedName)
            : new CallerIdentitySelectionResult(false, null, null);
    }

    public void ShowDuplicateCallerWarning(IWin32Window? owner)
    {
        MessageBox.Show(
            owner,
            "This caller is already in the list.",
            "Duplicate",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}
