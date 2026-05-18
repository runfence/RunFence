using System.Runtime.ExceptionServices;
using RunFence.Account.UI.Forms;

namespace RunFence.Account.UI;

public sealed class AccountCreationProgressRunner : IAccountCreationProgressRunner
{
    public async Task RunAsync(Func<IAccountCreationProgressReporter, Task> operation)
    {
        using var progressForm = new AccountCreationProgressForm();
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

    private sealed class FormProgressReporter(AccountCreationProgressForm form) : IAccountCreationProgressReporter
    {
        public CancellationToken CancellationToken => form.CancellationToken;

        public void SetStatus(string message) => form.SetStatus(message);
    }
}
