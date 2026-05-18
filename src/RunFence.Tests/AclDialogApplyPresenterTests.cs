using RunFence.Acl;
using RunFence.Acl.UI;
using Xunit;

namespace RunFence.Tests;

public class AclDialogApplyPresenterTests
{
    [Fact]
    public void ShowResult_Success_DoesNotShowWarning()
    {
        var presenter = new TestPresenter();

        var result = presenter.ShowResult(new NullWin32Window(), new AclApplyOutcome(true, [], []));

        Assert.Equal(DialogApplyPresentationStatus.RenderedSuccess, result.Status);
        Assert.Null(presenter.LastMessage);
        Assert.Null(presenter.LastTitle);
    }

    [Fact]
    public void ShowResult_SuccessWithWarnings_ShowsWarningWithoutErrors()
    {
        var presenter = new TestPresenter();
        var warning = new GrantApplyWarning(
            GrantApplyFailureStep.PostGrantMutationSave,
            @"C:\Warning",
            @"C:\Configs\extra.rfn",
            new InvalidOperationException("save failed"));

        var result = presenter.ShowResult(new NullWin32Window(), new AclApplyOutcome(true, [], [warning]));

        Assert.Equal(DialogApplyPresentationStatus.RenderedWarning, result.Status);
        Assert.Equal("Apply Warnings", presenter.LastTitle);
        Assert.NotNull(presenter.LastMessage);
        Assert.Contains("could not be saved durably", presenter.LastMessage, StringComparison.Ordinal);
        Assert.Contains(GrantApplyFailureFormatter.Format(warning), presenter.LastMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Errors:", presenter.LastMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowResult_FailureWithWarningsAndErrors_ShowsSeparateSections()
    {
        var presenter = new TestPresenter();
        var warning = new GrantApplyWarning(
            GrantApplyFailureStep.PostGrantMutationSave,
            @"C:\Warn",
            null,
            new InvalidOperationException("save warning"));
        var error = new GrantOperationException(
            GrantApplyFailureStep.GrantIntentSave,
            @"C:\Fail",
            @"C:\Configs\extra.rfn",
            new InvalidOperationException("save failed"));
        error.AppendCleanupFailure(
            GrantApplyFailureStep.RevertIntentSave,
            @"C:\Fail",
            null,
            new InvalidOperationException("rollback failed"));

        var result = presenter.ShowResult(new NullWin32Window(), new AclApplyOutcome(false, [error], [warning]));

        Assert.Equal(DialogApplyPresentationStatus.RenderedFailure, result.Status);
        Assert.Equal("Apply Errors", presenter.LastTitle);
        Assert.NotNull(presenter.LastMessage);
        Assert.Contains("Warnings:", presenter.LastMessage, StringComparison.Ordinal);
        Assert.Contains("Errors:", presenter.LastMessage, StringComparison.Ordinal);
        Assert.Contains(GrantApplyFailureFormatter.Format(warning), presenter.LastMessage, StringComparison.Ordinal);
        Assert.Contains(
            GrantApplyFailureFormatter.Format(error.Step, error.Path, error.ConfigPath, error.Cause),
            presenter.LastMessage,
            StringComparison.Ordinal);
        Assert.Contains("Cleanup failures:", presenter.LastMessage, StringComparison.Ordinal);
        Assert.Contains(
            GrantApplyFailureFormatter.Format(error.CleanupFailures[0]),
            presenter.LastMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ShowResult_FailureWithWarningsOnly_ShowsWarnings()
    {
        var presenter = new TestPresenter();
        var warning = new GrantApplyWarning(
            GrantApplyFailureStep.PostGrantRemoveSave,
            @"C:\WarnOnly",
            null,
            new InvalidOperationException("save warning"));

        var result = presenter.ShowResult(new NullWin32Window(), new AclApplyOutcome(false, [], [warning]));

        Assert.Equal(DialogApplyPresentationStatus.RenderedFailure, result.Status);
        Assert.Equal("Apply Warnings", presenter.LastTitle);
        Assert.NotNull(presenter.LastMessage);
        Assert.Contains("did not complete", presenter.LastMessage, StringComparison.Ordinal);
        Assert.Contains(GrantApplyFailureFormatter.Format(warning), presenter.LastMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowResult_UnexpectedError_ShowsPartialFailureAndRetainsPendingInput()
    {
        var presenter = new TestPresenter();
        var error = new GrantOperationException(
            GrantApplyFailureStep.GrantAclApply,
            @"C:\Fatal",
            null,
            new InvalidOperationException("unexpected"));

        var result = presenter.ShowResult(new NullWin32Window(), new AclApplyOutcome(false, [error], Array.Empty<GrantApplyWarning>()));

        Assert.Equal(DialogApplyPresentationStatus.RenderedFailure, result.Status);
        Assert.True(result.RetainPendingInput);
        Assert.Equal("Apply Errors", presenter.LastTitle);
        Assert.NotNull(presenter.LastMessage);
        Assert.Contains("The following operations failed (changes were partially applied)", presenter.LastMessage, StringComparison.Ordinal);
        Assert.Contains(GrantApplyFailureFormatter.Format(error.Step, error.Path, error.ConfigPath, error.Cause), presenter.LastMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowResult_FailureWithoutErrors_DoesNotShowWarning()
    {
        var presenter = new TestPresenter();

        var result = presenter.ShowResult(new NullWin32Window(), new AclApplyOutcome(false, [], []));

        Assert.Equal(DialogApplyPresentationStatus.RenderedFailure, result.Status);
        Assert.Null(presenter.LastMessage);
        Assert.Null(presenter.LastTitle);
    }

    private sealed class TestPresenter : AclDialogApplyPresenter
    {
        public string? LastTitle { get; private set; }
        public string? LastMessage { get; private set; }

        protected override void ShowMessage(IWin32Window owner, string title, string message)
        {
            LastTitle = title;
            LastMessage = message;
        }
    }

    private sealed class NullWin32Window : IWin32Window
    {
        public nint Handle => nint.Zero;
    }
}
