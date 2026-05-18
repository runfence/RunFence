using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.SidMigration.UI;

public sealed class SidMigrationProgressCoordinator(
    ILoggingService log,
    IMessageBoxService messageBoxService,
    SidMigrationDiskApplyController diskApplyController) : IDisposable
{
    private CancellationTokenSource? _cts;
    private readonly OperationGuard _operationGuard = new();
    private bool _goBackAfterOperationCancel;

    public bool IsInProgress => _operationGuard.IsInProgress;

    public (ProgressBar progressBar, Label statusLabel, CancellationToken ct) BeginProgressStep(
        ISidMigrationProgressStepView step,
        string statusText = "Scanning...",
        int? maxValue = null,
        bool showCancelButton = true)
    {
        _goBackAfterOperationCancel = false;
        step.Configure(statusText, maxValue, showCancelButton);

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        step.CancelButton.Click += (_, _) => _cts?.Cancel();

        _operationGuard.Begin();
        return (step.ProgressBar, step.StatusLabel, _cts.Token);
    }

    public async Task RunGuardedAsync(
        Func<Task> operation,
        string errorLogPrefix,
        ISidMigrationProgressStepView step,
        Action onCompleted,
        Action onCancel,
        Action onNavigateBackAfterCancel,
        bool resetProgressOnCancel = true)
    {
        var completed = false;
        var canceled = false;
        Exception? error = null;

        try
        {
            await operation();
            completed = true;
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        catch (Exception ex)
        {
            log.Error($"{errorLogPrefix} failed", ex);
            error = ex;
        }
        finally
        {
            _operationGuard.End();
        }

        if (step.View.IsDisposed)
            return;

        if (completed)
        {
            _goBackAfterOperationCancel = false;
            onCompleted();
            return;
        }

        if (canceled)
        {
            if (_goBackAfterOperationCancel)
            {
                _goBackAfterOperationCancel = false;
                onNavigateBackAfterCancel();
                return;
            }

            if (resetProgressOnCancel)
            {
                step.ProgressBar.Style = ProgressBarStyle.Continuous;
                step.ProgressBar.Value = 0;
            }

            onCancel();
            return;
        }

        if (error == null)
            return;

        _goBackAfterOperationCancel = false;
        step.ProgressBar.Style = ProgressBarStyle.Continuous;
        step.ProgressBar.Value = 0;
        step.StatusLabel.Text = $"Error: {error.Message}";
    }

    public bool TryHandleSecondaryAction(int currentStep, bool canShowBack, IWin32Window owner)
    {
        if (!_operationGuard.IsInProgress)
            return false;

        if (currentStep == 6)
        {
            if (_cts != null)
            {
                diskApplyController.TryRequestCancellation(_cts,
                    () => messageBoxService.Show(
                            owner,
                            "Step 6 is still applying irreversible filesystem changes.\n\nAbort the remaining apply work?",
                            "Abort Remaining Apply Work",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning) == DialogResult.Yes);
            }
            return true;
        }

        _goBackAfterOperationCancel = canShowBack;
        _cts?.Cancel();
        return true;
    }

    public void ShowStep6CloseBlockedMessage(IWin32Window owner)
    {
        messageBoxService.Show(
            owner,
            "Step 7 (In-App Migration) must be completed before closing.\n\n" +
            "Click \"Next\" to proceed to the in-app migration step, which updates RunFence's " +
            "internal data to match the disk changes applied in this step.",
            "Cannot Close",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _cts = null;
    }
}
