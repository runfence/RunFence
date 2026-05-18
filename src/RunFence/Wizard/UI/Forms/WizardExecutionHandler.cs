using RunFence.Wizard.UI.Forms.Steps;

namespace RunFence.Wizard.UI.Forms;

/// <summary>
/// Handles template execution lifecycle: running the template asynchronously,
/// showing the completion step, and running per-step async commit hooks.
/// </summary>
public class WizardExecutionHandler
{
    private IWizardExecutionContext _ctx = null!;
    private CancellationTokenSource? _executionCts;

    /// <summary>
    /// Binds the handler to the per-dialog execution context. Must be called before any operations.
    /// </summary>
    public void Initialize(IWizardExecutionContext ctx)
    {
        _ctx = ctx;
    }

    public void CancelExecution()
    {
        if (_executionCts == null)
            return;
        if (_executionCts.IsCancellationRequested)
            return;

        _ctx.SetCancelEnabled(false);
        _ctx.SetStatusText("Cancelling...");
        _executionCts.Cancel();
    }

    /// <summary>
    /// Executes the currently selected template asynchronously, then shows the completion step.
    /// </summary>
    public async Task ExecuteTemplateAsync()
    {
        _ctx.IsExecuting = true;
        _ctx.SetTitleText(_ctx.SelectedTemplate!.DisplayName);
        _ctx.SetProgressVisible(true);
        _ctx.SetBackEnabled(false);
        _ctx.SetNextEnabled(false);
        _ctx.SetCancelEnabled(true);
        _ctx.SetCancelText("Cancel");
        _ctx.HideError();

        var errors = new List<string>();
        var warnings = new List<string>();
        var statusMessages = new List<string>();
        bool wasCancelled = false;

        using var executionCts = new CancellationTokenSource();
        _executionCts = executionCts;

        var reporter = new WizardProgressReporter(
            executionCts.Token,
            msg =>
            {
                statusMessages.Add(msg);
                _ctx.SetStatusText(msg);
            },
            msg =>
            {
                warnings.Add(msg);
                _ctx.SetStatusText($"Warning: {msg}");
            },
            msg =>
            {
                errors.Add(msg);
                _ctx.SetStatusText($"Error: {msg}");
            });

        try
        {
            await _ctx.SelectedTemplate!.ExecuteAsync(reporter);
            executionCts.Token.ThrowIfCancellationRequested();

            // TemplateCompletedCount is incremented regardless of whether the template reported
            // warnings or errors through the progress reporter (non-throwing path). Intentional:
            // CompletionStep shows those details, and the user can run another template or close.
            // Only unhandled exceptions (caught below) skip the count increment.
            _ctx.TemplateCompletedCount++;

            var postAction = _ctx.SelectedTemplate!.PostWizardAction;
            if (postAction != null)
                _ctx.PostWizardActions.Add(postAction);
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
            errors.Add("Wizard execution was cancelled.");
        }
        catch (Exception ex)
        {
            errors.Add($"Unexpected error: {ex.Message}");
        }
        finally
        {
            _executionCts = null;
            _ctx.IsExecuting = false;
            _ctx.SetProgressVisible(false);
            _ctx.SetNavigationEnabled(true);
            _ctx.SetCancelText("Cancel");
        }

        ShowCompletionStep(errors, warnings, statusMessages, wasCancelled);
    }

    /// <summary>
    /// Runs the step's optional mid-wizard async commit hook before the wizard advances.
    /// Returns true to allow advancing; false if an error occurred.
    /// </summary>
    public async Task<bool> CommitStepAsync(WizardStepPage step)
    {
        _ctx.HideError();

        var reporter = new WizardProgressReporter(
            CancellationToken.None,
            msg => _ctx.SetStatusText(msg),
            msg => _ctx.SetStatusText($"Warning: {msg}"),
            msg => _ctx.ShowError(msg));

        Task task;
        try
        {
            task = step.OnCommitBeforeNextAsync(reporter);
        }
        catch (WizardReportedException)
        {
            return false; // error already reported by step/helper
        }
        catch (OperationCanceledException)
        {
            return false; // error already reported by step
        }
        catch (Exception ex)
        {
            _ctx.ShowError($"Step error: {ex.Message}");
            return false;
        }

        if (task.IsCompletedSuccessfully)
            return true;

        _ctx.IsExecuting = true;
        _ctx.SetProgressVisible(true);
        _ctx.SetNavigationEnabled(false);
        _ctx.SetCancelText("Cancel");

        try
        {
            await task;
            return true;
        }
        catch (WizardReportedException)
        {
            return false; // error already reported by step/helper
        }
        catch (OperationCanceledException)
        {
            return false; // error already reported by step
        }
        catch (Exception ex)
        {
            _ctx.ShowError($"Step error: {ex.Message}");
            return false;
        }
        finally
        {
            _ctx.IsExecuting = false;
            _ctx.SetProgressVisible(false);
            _ctx.SetNavigationEnabled(true);
            _ctx.SetBackEnabled(_ctx.CurrentStepIndex > 0);
        }
    }

    private void ShowCompletionStep(
        List<string> errors,
        List<string> warnings,
        List<string> statusMessages,
        bool wasCancelled)
    {
        var summaryLines = new List<string>
        {
            wasCancelled
                ? $"\u26A0\uFE0F {_ctx.SelectedTemplate!.DisplayName} was cancelled."
                : errors.Count == 0 && warnings.Count == 0
                ? $"\u2705 {_ctx.SelectedTemplate!.DisplayName} completed successfully."
                : errors.Count == 0
                    ? $"\u26A0\uFE0F {_ctx.SelectedTemplate!.DisplayName} completed with {warnings.Count} warning(s)."
                    : warnings.Count == 0
                        ? $"\u26A0\uFE0F {_ctx.SelectedTemplate!.DisplayName} completed with {errors.Count} error(s)."
                        : $"\u26A0\uFE0F {_ctx.SelectedTemplate!.DisplayName} completed with {errors.Count} error(s) and {warnings.Count} warning(s)."
        };

        summaryLines.AddRange(statusMessages.Select(msg => $"\u2022 {msg}"));

        var summary = string.Join(Environment.NewLine, summaryLines);

        var completionStep = new CompletionStep(summary, warnings, errors);
        _ctx.Steps.Add(completionStep);
        _ctx.ShowStep(_ctx.Steps.Count - 1);

        _ctx.SetCompletionButtonsState(showNavigation: true, cancelText: "Close");
        _ctx.SetNextText("Done");
    }

    private sealed class WizardProgressReporter(
        CancellationToken cancellationToken,
        Action<string> onStatus,
        Action<string> onWarning,
        Action<string> onError) : IWizardProgressReporter
    {
        public CancellationToken CancellationToken => cancellationToken;
        public void ReportStatus(string message) => onStatus(message);
        public void ReportWarning(string message) => onWarning(message);
        public void ReportError(string message) => onError(message);
    }
}
