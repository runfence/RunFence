using System.Runtime.ExceptionServices;
using RunFence.UI.Forms;

namespace RunFence.Account.UI;

public interface IWindowsTerminalDeploymentProgressRunner
{
    Task RunAsync(string initialStatus, Func<CancellationToken, Task> operation);
    Task<T> RunAsync<T>(string initialStatus, Func<CancellationToken, Task<T>> operation);
}

public sealed class WindowsTerminalDeploymentProgressRunner : IWindowsTerminalDeploymentProgressRunner
{
    private static readonly TimeSpan DialogDelay = TimeSpan.FromMilliseconds(400);

    public async Task RunAsync(string initialStatus, Func<CancellationToken, Task> operation)
        => await RunAsync<object?>(initialStatus, async cancellationToken =>
        {
            await operation(cancellationToken).ConfigureAwait(false);
            return null;
        }).ConfigureAwait(true);

    public async Task<T> RunAsync<T>(string initialStatus, Func<CancellationToken, Task<T>> operation)
    {
        using var progressForm = new CancellableProgressForm("Windows Terminal", initialStatus);
        var completionScheduler = SynchronizationContext.Current != null
            ? TaskScheduler.FromCurrentSynchronizationContext()
            : TaskScheduler.Current;
        Exception? failure = null;
        T? result = default;
        var operationTask = Task.Run(async () =>
        {
            try
            {
                result = await operation(progressForm.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        if (await Task.WhenAny(operationTask, Task.Delay(DialogDelay)).ConfigureAwait(true) != operationTask)
        {
            _ = operationTask.ContinueWith(
                _ =>
                {
                    if (!progressForm.IsDisposed)
                        progressForm.DialogResult = DialogResult.OK;
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                completionScheduler);

            await progressForm.ShowDialogAsync();
        }

        await operationTask.ConfigureAwait(true);
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();

        return result!;
    }
}
