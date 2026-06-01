namespace RunFence.UI.Forms;

public partial class CancellableProgressForm : ContextHelpForm
{
    private readonly CancellationTokenSource _cts = new();

    public CancellableProgressForm(string title, string initialStatus) : this()
    {
        Text = title;
        SetStatus(initialStatus);
    }

    public CancellationToken CancellationToken => _cts.Token;

    public void SetStatus(string message)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired && IsHandleCreated)
        {
            BeginInvoke(() => SetStatus(message));
            return;
        }

        _statusLabel.Text = message;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _cancelButton.Left = (_buttonPanel.ClientSize.Width - _cancelButton.Width) / 2;
        _cancelButton.Top = (_buttonPanel.ClientSize.Height - _cancelButton.Height) / 2;
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        _cancelButton.Enabled = false;
        _cts.Cancel();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
            e.Cancel = true;
        else
            base.OnFormClosing(e);
    }
}
