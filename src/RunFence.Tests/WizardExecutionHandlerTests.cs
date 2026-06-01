using RunFence.Wizard;
using RunFence.Wizard.UI.Forms;
using RunFence.Wizard.UI.Forms.Steps;
using Xunit;

namespace RunFence.Tests;

public class WizardExecutionHandlerTests
{
    [Fact]
    public async Task ExecuteTemplateAsync_WarningOnly_ShowsCompletionWarningWithoutErrorSummary()
    {
        var template = new TestTemplate(progress =>
        {
            progress.ReportStatus("Installing packages...");
            progress.ReportWarning("The package installer started, but RunFence could not finish some post-launch maintenance.");
            return Task.CompletedTask;
        });
        var context = new TestWizardExecutionContext { SelectedTemplate = template };
        var handler = new WizardExecutionHandler();
        handler.Initialize(context);

        await handler.ExecuteTemplateAsync();

        var completionStep = Assert.IsType<CompletionStep>(Assert.Single(context.Steps));
        var labels = completionStep.Controls.OfType<Label>().Select(l => l.Text).ToList();
        Assert.Contains(labels, text => text.Contains("completed with 1 warning(s)", StringComparison.Ordinal));
        Assert.DoesNotContain(labels, text => text.Contains("error(s)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteTemplateAsync_WithAsyncPostWizardAction_QueuesAwaitablePostAction()
    {
        var postActionInvoked = false;
        var template = new TestTemplate(
            _ => Task.CompletedTask,
            async _ =>
            {
                await Task.Yield();
                postActionInvoked = true;
            });
        var context = new TestWizardExecutionContext { SelectedTemplate = template };
        var handler = new WizardExecutionHandler();
        handler.Initialize(context);

        await handler.ExecuteTemplateAsync();

        var postAction = Assert.Single(context.PostWizardActions);
        await postAction(null!);
        Assert.True(postActionInvoked);
    }

    [Fact]
    public async Task CommitStepAsync_ReportedException_ReturnsFalseWithoutWrappingError()
    {
        var context = new TestWizardExecutionContext();
        var handler = new WizardExecutionHandler();
        handler.Initialize(context);

        var step = new TestStep(_ => throw new WizardReportedException("already shown"));

        var result = await handler.CommitStepAsync(step);

        Assert.False(result);
        Assert.Null(context.LastShownError);
    }

    [Fact]
    public async Task CommitStepAsync_ReportedExceptionFromAsyncCommit_ReturnsFalseWithoutWrappingError()
    {
        var context = new TestWizardExecutionContext();
        var handler = new WizardExecutionHandler();
        handler.Initialize(context);

        var step = new TestStep(_ => Task.FromException(new WizardReportedException("already shown")));

        var result = await handler.CommitStepAsync(step);

        Assert.False(result);
        Assert.Null(context.LastShownError);
    }

    [Fact]
    public async Task CancelExecution_ActiveTemplateExecution_DisablesCancelAndRequestsCancellation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var template = new TestTemplate(async progress =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, progress.CancellationToken);
        });
        var context = new TestWizardExecutionContext { SelectedTemplate = template };
        var handler = new WizardExecutionHandler();
        handler.Initialize(context);

        var executionTask = handler.ExecuteTemplateAsync();
        await started.Task;

        handler.CancelExecution();
        await executionTask;

        var completionStep = Assert.IsType<CompletionStep>(Assert.Single(context.Steps));
        var labels = completionStep.Controls.OfType<Label>().Select(l => l.Text).ToList();

        Assert.False(context.CancelEnabled);
        Assert.Equal("Cancelling...", context.LastStatusText);
        Assert.Contains(labels, text => text.Contains("was cancelled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CommitStepAsync_ClearsPreviousErrorBeforeRunningAsyncCommit()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new TestWizardExecutionContext();
        context.ShowError("stale error");
        var handler = new WizardExecutionHandler();
        handler.Initialize(context);

        var step = new TestStep(async _ =>
        {
            started.SetResult();
            await completed.Task;
        });

        var commitTask = handler.CommitStepAsync(step);
        await started.Task;

        Assert.Null(context.LastShownError);

        completed.SetResult();
        var result = await commitTask;

        Assert.True(result);
    }

    [Fact]
    public async Task ExecuteTemplateAsync_CancellationRequestedButSwallowedByTemplate_ShowsCancelledAndSkipsCompletionCount()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var template = new TestTemplate(async progress =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, progress.CancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        });
        var context = new TestWizardExecutionContext { SelectedTemplate = template };
        var handler = new WizardExecutionHandler();
        handler.Initialize(context);

        var executionTask = handler.ExecuteTemplateAsync();
        await started.Task;

        handler.CancelExecution();
        await executionTask;

        var completionStep = Assert.IsType<CompletionStep>(Assert.Single(context.Steps));
        var labels = completionStep.Controls.OfType<Label>().Select(l => l.Text).ToList();

        Assert.Equal(0, context.TemplateCompletedCount);
        Assert.Contains(labels, text => text.Contains("was cancelled", StringComparison.Ordinal));
    }

    private sealed class TestTemplate(
        Func<IWizardProgressReporter, Task> execute,
        Func<IWin32Window, Task>? postWizardAction = null) : IWizardTemplate
    {
        public string DisplayName => "Test Template";
        public string Description => string.Empty;
        public string IconEmoji => string.Empty;
        public Func<IWin32Window, Task>? PostWizardAction => postWizardAction;

        public IReadOnlyList<WizardStepPage> CreateSteps() => [];
        public Task ExecuteAsync(IWizardProgressReporter progress) => execute(progress);
        public void Cleanup() { }
    }

    private sealed class TestWizardExecutionContext : IWizardExecutionContext
    {
        public int CurrentStepIndex { get; set; }
        public List<WizardStepPage> Steps { get; } = [];
        public IWizardTemplate? SelectedTemplate { get; set; }
        public IReadOnlyList<IWizardTemplate> Templates { get; } = [];
        public bool IsExecuting { get; set; }
        public List<Func<IWin32Window, Task>> PostWizardActions { get; } = [];
        public int TemplateCompletedCount { get; set; }
        public string? LastStatusText { get; private set; }
        public string? LastShownError { get; private set; }
        public bool CancelEnabled { get; private set; } = true;

        public void ShowStep(int index)
        {
            CurrentStepIndex = index;
        }

        public void ShowError(string message)
        {
            LastShownError = message;
        }

        public void HideError()
        {
            LastShownError = null;
        }

        public void SetProgressVisible(bool visible) { }

        public void SetNavigationEnabled(bool enabled) { }

        public void SetNextEnabled(bool enabled) { }

        public void SetCancelEnabled(bool enabled)
        {
            CancelEnabled = enabled;
        }

        public void SetCancelText(string text) { }

        public void SetStatusText(string text)
        {
            LastStatusText = text;
        }

        public void SetCompletionButtonsState(bool showNavigation, string cancelText) { }

        public void SetBackEnabled(bool enabled) { }

        public void SetTitleText(string text) { }

        public void SetNextText(string text) { }

        public void InvalidateStepIndicator() { }

        public void BeginInvokeOnUI(Action action)
        {
            action();
        }

        public void UnsubscribeAndDispose(IEnumerable<WizardStepPage> steps) { }

        public void SubscribeStep(WizardStepPage step) { }
    }

    private sealed class TestStep(Func<IWizardProgressReporter, Task> commitAction) : WizardStepPage
    {
        public override string StepTitle => "Step";
        public override string? Validate() => null;
        public override void Collect()
        {
        }

        public override Task OnCommitBeforeNextAsync(IWizardProgressReporter progress) => commitAction(progress);
    }
}
