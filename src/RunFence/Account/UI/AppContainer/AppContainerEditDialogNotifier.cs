using RunFence.Account.UI;

namespace RunFence.Account.UI.AppContainer;

public class AppContainerEditDialogNotifier(IAccountMessageBoxService messageBoxService) : IAppContainerEditDialogNotifier
{
    public void ShowValidationWarning(IWin32Window owner, string message)
    {
        var caption = owner is IAppContainerEditDialogNotificationContext dialog
            ? dialog.PendingValidationCaption
            : "Validation";
        messageBoxService.Show(owner, message, caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    public void ShowOperationError(IWin32Window owner, string message)
    {
        messageBoxService.Show(owner, message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    public void ShowRestartRequired(IWin32Window owner)
    {
        messageBoxService.Show(
            owner,
            "Capability changes will take effect on next app launch.",
            "Restart Required",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    public void ShowComAccessWarning(IWin32Window owner, IReadOnlyList<string> warnings)
    {
        var message = owner is IAppContainerEditDialogNotificationContext { IsCreateMode: true }
            ? $"Some COM access entries could not be applied:\n\n{string.Join("\n", warnings)}"
            : $"Some AppContainer changes could not be applied:\n\n{string.Join("\n", warnings)}";
        messageBoxService.Show(
            owner,
            message,
            "COM Access Warning",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    public void ShowPersistenceWarning(IWin32Window owner, string message)
    {
        var caption = owner is IAppContainerEditDialogNotificationContext
        {
            IsCreateMode: true,
            PendingNotificationStatus: AppContainerOperationStatus.SaveFailedBeforeOs
        }
            ? "Save Failed"
            : "Warning";
        messageBoxService.Show(owner, message, caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
