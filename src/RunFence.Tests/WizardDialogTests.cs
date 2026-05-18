using RunFence.Tests.Helpers;
using RunFence.Wizard;
using RunFence.Wizard.UI.Forms;
using RunFence.Wizard.UI.Forms.Steps;
using Xunit;

namespace RunFence.Tests;

public class WizardDialogTests
{
    [Fact]
    public void Constructor_ShowsPickerImmediatelyAndPreservesHiddenTemplateFilteringUntilWarmupCompletes()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var warmupGate = new ManualResetEventSlim(false);
            var availableTemplate = new TestTemplate("Available", isAvailable: true, warmupGate);
            var hiddenTemplate = new TestTemplate("Hidden", isAvailable: false, warmupGate);

            using var dialog = new WizardDialog(
                [availableTemplate, hiddenTemplate],
                new WizardExecutionHandler(),
                new WizardNavigationHandler());

            StaTestHelper.CreateControlTree(dialog);
            Application.DoEvents();

            var picker = FindDescendant<TemplatePickerStep>(dialog);
            var nextButton = FindButton(dialog, "Next \u2192");
            var flowPanel = FindDescendant<FlowLayoutPanel>(picker);

            Assert.NotNull(picker);
            Assert.NotNull(nextButton);
            Assert.NotNull(flowPanel);
            Assert.True(dialog.UseWaitCursor);
            Assert.False(nextButton.Enabled);
            Assert.Empty(flowPanel.Controls);

            warmupGate.Set();

            StaTestHelper.PumpUntil(
                () => !dialog.UseWaitCursor && CountTemplateCards(flowPanel) == 1,
                timeoutMessage: "Timed out waiting for the wizard template picker to finish warming up.");

            Assert.False(nextButton.Enabled);
        });
    }

    [Fact]
    public void CancelButton_DuringTemplateExecution_CancelsAndShowsCompletionState()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var template = new CancelAwareTemplate();

            using var dialog = new WizardDialog(
                [template],
                new WizardExecutionHandler(),
                new WizardNavigationHandler());
            var context = (IWizardExecutionContext)dialog;
            context.SelectedTemplate = template;
            context.Steps.Add(new PassiveStep());

            StaTestHelper.CreateControlTree(dialog);
            context.ShowStep(1);

            var nextButton = FindButton(dialog, "Apply");
            StaTestHelper.ClickButton(nextButton);

            var cancelButton = FindButton(dialog, "Cancel");
            StaTestHelper.PumpUntil(
                () => template.ExecutionStarted && cancelButton.Enabled,
                timeoutMessage: "Timed out waiting for template execution to start.");

            StaTestHelper.ClickButton(cancelButton);

            Assert.True(template.CapturedToken.IsCancellationRequested);

            StaTestHelper.PumpUntil(
                () => template.CancellationObserved && FindAllDescendants<CompletionStep>(dialog).Any(),
                timeoutMessage: "Timed out waiting for wizard cancellation to complete.");

            var completionStep = FindDescendant<CompletionStep>(dialog);
            var errorsList = FindAllDescendants<ListBox>(completionStep).Single();
            var summaryLabel = FindAllDescendants<Label>(completionStep)
                .First(label => label.Text.Contains("cancelled", StringComparison.Ordinal));
            var doneButton = FindAllDescendants<Button>(dialog)
                .Single(button => string.Equals(button.Text, "Done", StringComparison.Ordinal));

            Assert.Contains("Wizard execution was cancelled.", errorsList.Items.Cast<object>().Select(item => item.ToString()));
            Assert.Contains("was cancelled", summaryLabel.Text, StringComparison.Ordinal);
            Assert.Equal(0, context.TemplateCompletedCount);
            Assert.Equal("Done", doneButton.Text);
            Assert.Equal("Close", cancelButton.Text);
        });
    }

    private static T FindDescendant<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T typed)
                return typed;

            if (TryFindDescendant(child, out typed))
                return typed;
        }

        throw new Xunit.Sdk.XunitException($"Could not find descendant control of type {typeof(T).Name}.");
    }

    private static bool TryFindDescendant<T>(Control root, out T result) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T typed)
            {
                result = typed;
                return true;
            }

            if (TryFindDescendant(child, out typed))
            {
                result = typed;
                return true;
            }
        }

        result = null!;
        return false;
    }

    private static Button FindButton(Control root, string text)
        => FindAllDescendants<Button>(root).Single(button => string.Equals(button.Text, text, StringComparison.Ordinal));

    private static IEnumerable<T> FindAllDescendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T typed)
                yield return typed;

            foreach (var descendant in FindAllDescendants<T>(child))
                yield return descendant;
        }
    }

    private static int CountTemplateCards(FlowLayoutPanel panel)
        => panel.Controls
            .OfType<Panel>()
            .Count(control => control.Cursor == Cursors.Hand);

    private sealed class TestTemplate(string displayName, bool isAvailable, ManualResetEventSlim warmupGate) : IWizardTemplate
    {
        public string DisplayName => displayName;
        public bool IsAvailable => isAvailable;
        public string Description => displayName;
        public string IconEmoji => "\u2605";
        public Action<IWin32Window>? PostWizardAction => null;

        public IReadOnlyList<WizardStepPage> CreateSteps() => [];

        public Task ExecuteAsync(IWizardProgressReporter progress) => Task.CompletedTask;

        public void Cleanup()
        {
        }

        public Task WarmCacheAsync() => Task.Run(() => warmupGate.Wait());
    }

    private sealed class CancelAwareTemplate : IWizardTemplate
    {
        public bool ExecutionStarted { get; private set; }
        public bool CancellationObserved { get; private set; }
        public CancellationToken CapturedToken { get; private set; }

        public string DisplayName => "Cancelable";
        public string Description => "Cancelable execution";
        public string IconEmoji => "\u2605";
        public Action<IWin32Window>? PostWizardAction => null;

        public IReadOnlyList<WizardStepPage> CreateSteps() => [];

        public async Task ExecuteAsync(IWizardProgressReporter progress)
        {
            ExecutionStarted = true;
            CapturedToken = progress.CancellationToken;
            try
            {
                await Task.Delay(Timeout.Infinite, progress.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }

        public void Cleanup()
        {
        }
    }

    private sealed class PassiveStep : WizardStepPage
    {
        public override string StepTitle => "Passive";
        public override string? Validate() => null;
        public override void Collect()
        {
        }
    }
}
