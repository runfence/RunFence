using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class StaTestHelperTests
{
    [Fact]
    public void CreateControlTree_WhenCalledForForm_DoesNotRequireShowingWindow()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var form = new Form();

            StaTestHelper.CreateControlTree(form);
            Application.DoEvents();

            Assert.False(form.Visible);
            Assert.True(form.IsHandleCreated);
        });
    }

    [Fact]
    public void RunOnSta_WhenVisibleFormIsDetected_FailsCurrentTest()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            StaTestHelper.RunOnSta(() =>
            {
                // Explicit exception to the no-real-UI test rule: this verifies the guard detects
                // and closes an actual Form.Show path, not only a synthetic signal.
                var form = new Form { Text = "Visible test form" };
                form.Show();
                StaTestHelper.PumpUntil(() => form.Visible);
            }));

        Assert.Contains("Unexpected visible WinForms window in test", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RunOnSta_WhenVisibleFormCleanupThrows_StillFailsForVisibleForm()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            StaTestHelper.RunOnSta(() =>
            {
                var form = new ThrowingDisposeForm { Text = "Visible cleanup failure form" };
                form.Show();
                StaTestHelper.PumpUntil(() => form.Visible);
            }));

        Assert.Contains("Unexpected visible WinForms window in test", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Cleanup of the visible form also failed", ex.Message, StringComparison.Ordinal);
        Assert.IsType<AggregateException>(ex.InnerException);
    }

    private sealed class ThrowingDisposeForm : Form
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                throw new InvalidOperationException("Dispose failed");
        }
    }

    [Fact]
    public void RunOnSta_WhenNativeDialogIsDetected_FailsCurrentTest()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            StaTestHelper.RunOnSta(() =>
            {
                // Explicit exception to the no-real-UI test rule: this verifies the guard detects
                // and closes an actual native MessageBox.Show path, not only a synthetic signal.
                MessageBox.Show("Headless violation", "Native Dialog Guard Test");
            }));

        Assert.Contains("Unexpected native dialog in test", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Class=", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RunOnSta_WhenNativeDialogIsDetectedAndActionThrows_StillFailsForHeadlessViolation()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            StaTestHelper.RunOnSta(() =>
            {
                // Explicit exception to the no-real-UI test rule: this verifies the guard detects
                // and closes an actual native MessageBox.Show path, not only a synthetic signal.
                MessageBox.Show("Headless violation", "Native Dialog Guard Test");
                throw new InvalidOperationException("Original test failure");
            }));

        Assert.Contains("Unexpected native dialog in test", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Original test failure", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RunOnSta_WhenWinFormsCallbackThrowsUnhandledException_FailsCurrentTest()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            StaTestHelper.RunOnSta(() =>
            {
                using var form = new Form();
                StaTestHelper.CreateControlTree(form);

                _ = form.BeginInvoke(new Action(() =>
                    throw new InvalidOperationException("Unhandled callback failure")));

                StaTestHelper.PumpUntil(
                    () =>
                    {
                        Application.DoEvents();
                        return true;
                    });
            }));

        Assert.Contains("Unexpected unhandled WinForms exception in test", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Unhandled callback failure", ex.Message, StringComparison.Ordinal);
    }
}
