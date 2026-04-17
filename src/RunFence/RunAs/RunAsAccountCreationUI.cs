using RunFence.Account;
using RunFence.Account.UI.AppContainer;
using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.RunAs;

/// <summary>
/// Handles dialog creation and display for the RunAs account and container creation flows.
/// </summary>
/// <remarks>Asymmetric by design: account dialog needs post-creation credential flow (password entry → encrypt → save), container dialog does not (profile created by OS).</remarks>
public class RunAsAccountCreationUI(
    Func<EditAccountDialog> editAccountDialogFactory,
    AppContainerEditService containerEditService,
    IModalCoordinator modalCoordinator)
{
    /// <summary>
    /// Shows the EditAccountDialog for account creation wrapped with <see cref="IModalCoordinator.BeginModal"/>.
    /// On success (DialogResult.OK and CreatedSid set), returns the dialog WITH modal still active —
    /// the caller is responsible for calling <see cref="IModalCoordinator.EndModal"/> in a finally block
    /// that also wraps all post-dialog work (permission prompts, settings application).
    /// On cancel, EndModal is called here and the result has <see cref="ShowCreateAccountResult.WasCancelled"/> set.
    /// Caller must dispose the returned dialog.
    /// </summary>
    public ShowCreateAccountResult ShowCreateAccountDialog(string filePath, RunAsDosProtection dosProtection)
    {
        var prefillUsername = UsernameHelper.GenerateFromPath(filePath);
        EditAccountDialog? dlg = null;
        modalCoordinator.BeginModal();
        try
        {
            dlg = editAccountDialogFactory();
            dlg.InitializeForCreate(prefillUsername);
            dlg.StartPosition = FormStartPosition.CenterScreen;
            dlg.Shown += (_, _) => { WindowForegroundHelper.ForceToForeground(dlg.Handle); dlg.BringToFront(); };
            var result = dlg.ShowDialog();

            if (result != DialogResult.OK || dlg.CreatedSid == null)
            {
                dosProtection.RecordDecline();
                dlg.Dispose();
                modalCoordinator.EndModal();
                return new ShowCreateAccountResult(null, WasCancelled: true);
            }

            // Modal remains active — caller owns EndModal to cover post-dialog work.
            return new ShowCreateAccountResult(dlg, WasCancelled: false);
        }
        catch
        {
            dlg?.Dispose();
            modalCoordinator.EndModal();
            throw;
        }
    }

    /// <summary>
    /// Shows the AppContainerEditDialog for container creation.
    /// Returns the created entry on success, or null if cancelled.
    /// </summary>
    public AppContainerEntry? ShowCreateContainerDialog()
    {
        modalCoordinator.BeginModal();
        AppContainerEditDialog? dlg = null;
        try
        {
            dlg = new AppContainerEditDialog(null, containerEditService);
            dlg.StartPosition = FormStartPosition.CenterScreen;
            dlg.Shown += (_, _) => { WindowForegroundHelper.ForceToForeground(dlg.Handle); dlg.BringToFront(); };
            var result = dlg.ShowDialog();

            if (result != DialogResult.OK)
                return null;
            return dlg.CreatedEntry;
        }
        finally
        {
            modalCoordinator.EndModal();
            dlg?.Dispose();
        }
    }
}
