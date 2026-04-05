using RunFence.Account;
using RunFence.Account.UI.AppContainer;
using RunFence.Account.UI.Forms;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;
using RunFence.UI.Forms;

namespace RunFence.RunAs;

/// <summary>
/// Handles dialog creation and display for the RunAs account and container creation flows.
/// </summary>
public class RunAsAccountCreationUI(
    IAppContainerService appContainerService,
    Func<EditAccountDialog> editAccountDialogFactory,
    AppContainerEditService containerEditService)
{
    /// <summary>
    /// Shows the EditAccountDialog for account creation.
    /// Returns the dialog on success (DialogResult.OK and CreatedSid set), or null if cancelled.
    /// Caller must dispose the returned dialog.
    /// Caller is responsible for BeginModal/EndModal tracking around the entire operation.
    /// </summary>
    public EditAccountDialog? ShowCreateAccountDialog(string filePath, RunAsDosProtection dosProtection)
    {
        var prefillUsername = UsernameHelper.GenerateFromPath(filePath);
        EditAccountDialog? dlg = null;
        try
        {
            dlg = editAccountDialogFactory();
            dlg.InitializeForCreate(prefillUsername);
            dlg.StartPosition = FormStartPosition.CenterScreen;
            dlg.Shown += (_, _) => NativeInterop.ForceToForeground(dlg);
            var result = dlg.ShowDialog();

            if (result != DialogResult.OK || dlg.CreatedSid == null)
            {
                dosProtection.RecordDecline();
                dlg.Dispose();
                return null;
            }

            return dlg;
        }
        catch
        {
            dlg?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Shows the AppContainerEditDialog for container creation.
    /// Returns the created entry on success, or null if cancelled.
    /// </summary>
    public AppContainerEntry? ShowCreateContainerDialog()
    {
        DataPanel.BeginModal();
        AppContainerEditDialog? dlg = null;
        try
        {
            dlg = new AppContainerEditDialog(null, appContainerService, containerEditService);
            dlg.StartPosition = FormStartPosition.CenterScreen;
            dlg.Shown += (_, _) => NativeInterop.ForceToForeground(dlg);
            var result = dlg.ShowDialog();

            if (result != DialogResult.OK)
                return null;
            return dlg.CreatedEntry;
        }
        finally
        {
            DataPanel.EndModal();
            dlg?.Dispose();
        }
    }
}