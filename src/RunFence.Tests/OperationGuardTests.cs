using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class OperationGuardTests
{
    [Fact]
    public void Begin_DisablesOwnerControl()
    {
        var guard = new OperationGuard();
        var control = new Button { Enabled = true };

        guard.Begin(control);

        Assert.True(guard.IsInProgress);
        Assert.False(control.Enabled);

        // Cleanup
        guard.End(control);
    }

    [Fact]
    public void End_ReEnablesOwnerControl()
    {
        var guard = new OperationGuard();
        var control = new Button { Enabled = true };

        guard.Begin(control);
        guard.End(control);

        Assert.False(guard.IsInProgress);
        Assert.True(control.Enabled);
    }

    [Fact]
    public void End_SkipsReEnableWhenOwnerDisposed()
    {
        var guard = new OperationGuard();
        var control = new Button { Enabled = true };

        guard.Begin(control);
        control.Dispose();
        guard.End(control);

        Assert.False(guard.IsInProgress);
    }

    [Fact]
    public void Begin_HooksFormClosing_PreventsUserClose()
    {
        var guard = new OperationGuard();
        using var form = new Form();
        var panel = new Panel();
        form.Controls.Add(panel);

        guard.Begin(panel);

        // Simulate FormClosing by evaluating close-prevention logic directly (no MessageBox)
        var args = new FormClosingEventArgs(CloseReason.UserClosing, false);
        Assert.True(guard.ShouldPreventClose(args));

        guard.End(panel);
    }

    [Fact]
    public void Begin_FormClosing_AllowsNonUserClose()
    {
        var guard = new OperationGuard();
        using var form = new Form();
        var panel = new Panel();
        form.Controls.Add(panel);

        guard.Begin(panel);

        // Application-initiated close should not be cancelled
        var args = new FormClosingEventArgs(CloseReason.ApplicationExitCall, false);
        Assert.False(guard.ShouldPreventClose(args));

        guard.End(panel);
    }

    [Fact]
    public void End_UnhooksFormClosing_AllowsClose()
    {
        var guard = new OperationGuard();
        using var form = new Form();
        var panel = new Panel();
        form.Controls.Add(panel);

        guard.Begin(panel);
        guard.End(panel);

        // After End, IsInProgress is false — ShouldPreventClose returns false
        var args = new FormClosingEventArgs(CloseReason.UserClosing, false);
        Assert.False(guard.ShouldPreventClose(args));
    }
}