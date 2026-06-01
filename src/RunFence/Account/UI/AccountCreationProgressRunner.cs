using System.Runtime.ExceptionServices;
using RunFence.UI.Forms;

namespace RunFence.Account.UI;

public sealed class AccountCreationProgressRunner : IAccountCreationProgressRunner
{
    public async Task RunAsync(Func<IAccountCreationProgressReporter, Task> operation)
    {
        using var progressForm = new CancellableProgressForm("Creating Account", "Please wait...");
        Exception? failure = null;

        progressForm.Shown += async (_, _) =>
        {
            try
            {
                await operation(new FormProgressReporter(progressForm));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                progressForm.DialogResult = DialogResult.OK;
            }
        };

        await progressForm.ShowDialogAsync();

        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class FormProgressReporter(CancellableProgressForm form) : IAccountCreationProgressReporter
    {
        public CancellationToken CancellationToken => form.CancellationToken;

        public void SetStatus(string message) => form.SetStatus(message);
    }
}
