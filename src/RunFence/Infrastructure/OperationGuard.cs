namespace RunFence.Infrastructure;

/// <summary>
/// Tracks whether a long-running operation is in progress, disables the
/// owner control to prevent reentrancy, and prevents form close.
/// </summary>
public class OperationGuard
{
    private volatile bool _inProgress;

    public bool IsInProgress => _inProgress;

    /// <summary>
    /// Marks the operation as started, hooks FormClosing to prevent close,
    /// and disables the owner control to prevent reentrancy.
    /// </summary>
    public void Begin(Control owner)
    {
        _inProgress = true;
        owner.Enabled = false;
        var form = owner.FindForm();
        if (form != null)
            form.FormClosing += PreventCloseWhileBusy;
    }

    /// <summary>
    /// Marks the operation as started without disabling any control.
    /// Use when the form has its own OnFormClosing override and a cancel
    /// button inside the step panel must remain interactive.
    /// </summary>
    public void Begin()
    {
        _inProgress = true;
    }

    /// <summary>
    /// Marks the operation as finished, re-enables the owner control,
    /// and unhooks FormClosing.
    /// </summary>
    public void End(Control owner)
    {
        _inProgress = false;
        if (!owner.IsDisposed)
            owner.Enabled = true;
        var form = owner.FindForm();
        if (form != null)
            form.FormClosing -= PreventCloseWhileBusy;
    }

    /// <summary>
    /// Marks the operation as finished. Pair with <see cref="Begin()"/>.
    /// </summary>
    public void End()
    {
        _inProgress = false;
    }

    private void PreventCloseWhileBusy(object? sender, FormClosingEventArgs e)
    {
        if (!ShouldPreventClose(e))
            return;
        e.Cancel = true;
        MessageBox.Show("Please wait for the current operation to complete.",
            "Busy", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public bool ShouldPreventClose(FormClosingEventArgs e)
    {
        return _inProgress && e.CloseReason == CloseReason.UserClosing;
    }
}