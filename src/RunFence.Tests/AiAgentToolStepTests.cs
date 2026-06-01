using Moq;
using RunFence.Apps.UI;
using RunFence.Apps.Shortcuts;
using RunFence.Infrastructure;
using RunFence.Tests.Helpers;
using RunFence.Wizard;
using RunFence.Wizard.UI.Forms.Steps;
using Xunit;

namespace RunFence.Tests;

public class AiAgentToolStepTests
{
    [Fact]
    public void Collect_WithRelativeCommand_PreservesCommandText()
    {
        StaTestHelper.RunOnSta(() =>
        {
            bool? capturedUsePackage = null;
            string? capturedPath = null;
            using var step = CreateStep((usePackage, path) =>
            {
                capturedUsePackage = usePackage;
                capturedPath = path;
            });

            SelectOtherTool(step, "claude");
            step.Collect();

            Assert.False(capturedUsePackage);
            Assert.Equal("claude", capturedPath);
        });
    }

    [Fact]
    public void Collect_WithRootedExistingPath_NormalizesPath()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tempDir = new TempDirectory("AiAgentToolStep");
            var exePath = Path.Combine(tempDir.Path, "tool.exe");
            File.WriteAllText(exePath, "");

            string? capturedPath = null;
            using var step = CreateStep((_, path) => capturedPath = path);
            SelectOtherTool(step, Path.Combine(tempDir.Path, ".", "tool.exe"));

            step.Collect();

            Assert.Equal(Path.GetFullPath(exePath), capturedPath);
        });
    }

    [Fact]
    public void OnCommitBeforeNextAsync_WithMissingRootedPath_ThrowsReportedExceptionAndReportsError()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var step = CreateStep((_, _) => { });
            SelectOtherTool(step, @"C:\does-not-exist\tool.exe");
            var progress = new RecordingProgressReporter();

            var ex = Assert.Throws<WizardReportedException>(() =>
            {
                _ = step.OnCommitBeforeNextAsync(progress);
            });

            Assert.Contains("does not exist", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(progress.Errors, error => error.Contains("does not exist", StringComparison.OrdinalIgnoreCase));
        });
    }

    private static AiAgentToolStep CreateStep(Action<bool, string?> capture)
        => new(
            capture,
            Mock.Of<IShortcutDiscoveryService>(),
            Mock.Of<IShortcutIconHelper>(),
            Mock.Of<IOpenFileDialogAdapterFactory>(),
            Mock.Of<IFolderBrowserDialogAdapterFactory>(),
            Mock.Of<IAppDiscoveryDialogService>());

    private static void SelectOtherTool(AiAgentToolStep step, string pathText)
    {
        var otherToolRadio = FindControls<RadioButton>(step)
            .First(radio => radio.Text.StartsWith("Other tool", StringComparison.Ordinal));
        otherToolRadio.Checked = true;
        var browse = FindControls<AppPathBrowseControl>(step).Single();
        browse.PathText = pathText;
    }

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                yield return match;

            foreach (var nested in FindControls<T>(child))
                yield return nested;
        }
    }

    private sealed class RecordingProgressReporter : IWizardProgressReporter
    {
        public List<string> Errors { get; } = [];
        public CancellationToken CancellationToken => CancellationToken.None;

        public void ReportStatus(string message)
        {
        }

        public void ReportWarning(string message)
        {
        }

        public void ReportError(string message) => Errors.Add(message);
    }
}
