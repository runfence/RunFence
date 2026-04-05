#nullable disable

using System.ComponentModel;

namespace RunFence.Account.UI.Forms;

partial class AccountCreationProgressForm
{
    private IContainer components = null;

    private ProgressBar _progressBar;
    private Label _statusLabel;

    internal AccountCreationProgressForm() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new Container();
        _progressBar = new ProgressBar();
        _statusLabel = new Label();
        SuspendLayout();

        _progressBar.Dock = DockStyle.Top;
        _progressBar.Height = 20;
        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = 30;

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        _statusLabel.Padding = new Padding(8);
        _statusLabel.Text = "Please wait...";

        Controls.Add(_statusLabel);
        Controls.Add(_progressBar);

        Text = "Creating Account";
        ClientSize = new Size(380, 80);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        ResumeLayout(false);
    }
}
