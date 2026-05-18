using RunFence.Core.Models;

namespace RunFence.Account.UI.AppContainer;

public class AppContainerDialogResultPresenter(IAppContainerEditDialogNotifier notifier)
{
    internal DialogResult? ApplyResult(
        IAppContainerEditDialogResultContext context,
        IWin32Window owner,
        AppContainerEditSubmitResult submitResult)
    {
        context.LastOperationStatus = submitResult.OperationStatus;
        context.PendingNotificationStatus = submitResult.OperationStatus;
        context.CreatedEntry = submitResult.CreatedEntry;

        if (submitResult.ValidationMessage != null)
            notifier.ShowValidationWarning(owner, submitResult.ValidationMessage);

        if (submitResult.PersistenceWarningText != null)
            notifier.ShowPersistenceWarning(owner, submitResult.PersistenceWarningText);

        if (submitResult.OperationErrorText != null)
            notifier.ShowOperationError(owner, submitResult.OperationErrorText);

        if (submitResult.ComAccessWarnings.Count > 0)
            notifier.ShowComAccessWarning(owner, submitResult.ComAccessWarnings);

        if (submitResult.RestartRequired)
            notifier.ShowRestartRequired(owner);

        var dialogResult = submitResult.DialogResult.GetValueOrDefault(DialogResult.None);
        return dialogResult == DialogResult.None ? null : dialogResult;
    }

    internal void ApplyUnhandledException(IWin32Window owner, Exception exception)
    {
        notifier.ShowOperationError(owner, exception.Message);
    }
}
