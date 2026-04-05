using RunFence.Wizard.UI.Forms.Steps;

namespace RunFence.Wizard.UI.Forms;

/// <summary>
/// Handles template execution lifecycle: running the template asynchronously,
/// showing the completion step, and running per-step async commit hooks.
/// </summary>
public class WizardExecutionHandler
{
    private IWizardExecutionContext _ctx = null!;

    /// <summary>
    /// Binds the handler to the per-dialog execution context. Must be called before any operations.
    /// </summary>
    public void Initialize(IWizardExecutionContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>
    /// Executes the currently selected template asynchronously, then shows the completion step.
    /// </summary>
    public async Task ExecuteTemplateAsync()
    {
        _ctx.IsExecuting = true;
        _ctx.SetProgressVisible(true);
        _ctx.SetNavigationEnabled(false);

        var errors = new List<string>();
        var statusMessages = new List<string>();

        var reporter = new WizardProgressReporter(
            msg =>
            {
                statusMessages.Add(msg);
                _ctx.SetStatusText(msg);
            },
            msg =>
            {
                errors.Add(msg);
                _ctx.SetStatusText($"Error: {msg}");
            });

        try
        {
            await _ctx.SelectedTemplate!.ExecuteAsync(reporter);

            _ctx.TemplateCompletedCount++;

            var postAction = _ctx.SelectedTemplate!.PostWizardAction;
            if (postAction != null)
                _ctx.PostWizardActions.Add(postAction);
        }
        catch (Exception ex)
        {
            errors.Add($"Unexpected error: {ex.Message}");
        }
        finally
        {
            _ctx.IsExecuting = false;
            _ctx.SetProgressVisible(false);
            _ctx.SetNavigationEnabled(true);
        }

        ShowCompletionStep(errors, statusMessages);
    }

    /// <summary>
    /// Runs the step's optional mid-wizard async commit hook before the wizard advances.
    /// Returns true to allow advancing; false if an error occurred.
    /// </summary>
    public async Task<bool> CommitStepAsync(WizardStepPage step)
    {
        var reporter = new WizardProgressReporter(
            msg => _ctx.SetStatusText(msg),
            msg => _ctx.ShowError(msg));

        Task task;
        try
        {
            task = step.OnCommitBeforeNextAsync(reporter);
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

        try
        {
            await task;
            return true;
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

    private void ShowCompletionStep(List<string> errors, List<string> statusMessages)
    {
        var summaryLines = new List<string>();
        summaryLines.Add(errors.Count == 0
            ? $"\u2705 {_ctx.SelectedTemplate!.DisplayName} completed successfully."
            : $"\u26A0\uFE0F {_ctx.SelectedTemplate!.DisplayName} completed with {errors.Count} error(s).");

        summaryLines.AddRange(statusMessages.Select(msg => $"  \u2022 {msg}"));

        var summary = string.Join(Environment.NewLine, summaryLines);

        var completionStep = new CompletionStep(summary, errors);
        _ctx.Steps.Add(completionStep);
        _ctx.ShowStep(_ctx.Steps.Count - 1);

        _ctx.SetCompletionButtonsState(showNavigation: false, cancelText: "Close");
    }

    private sealed class WizardProgressReporter(
        Action<string> onStatus,
        Action<string> onError) : IWizardProgressReporter
    {
        public void ReportStatus(string message) => onStatus(message);
        public void ReportError(string message) => onError(message);
    }
}