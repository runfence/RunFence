#nullable disable

using System.ComponentModel;

namespace RunFence.Account.UI.Forms;

partial class AccountCreationProgressForm
{
    private IContainer components = null;

    private ProgressBar _progressBar;
    private Label _statusLabel;
    private Panel _buttonPanel;
    private Button _cancelButton;

    internal AccountCreationProgressForm() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts?.Dispose();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new Container();
        _progressBar = new ProgressBar();
        _statusLabel = new Label();
        _buttonPanel = new Panel();
        _cancelButton = new Button();
        _buttonPanel.SuspendLayout();
        SuspendLayout();

        _progressBar.Dock = DockStyle.Top;
        _progressBar.Height = 20;
        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = 30;

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        _statusLabel.Padding = new Padding(8);
        _statusLabel.Text = "Please wait...";

        _cancelButton.Text = "Cancel";
        _cancelButton.Height = 28;
        _cancelButton.Width = 80;
        _cancelButton.FlatStyle = FlatStyle.System;
        _cancelButton.Anchor = AnchorStyles.None;
        _cancelButton.Click += OnCancelClick;

        _buttonPanel.Dock = DockStyle.Bottom;
        _buttonPanel.Height = 40;
        _buttonPanel.Controls.Add(_cancelButton);

        Controls.Add(_statusLabel);
        Controls.Add(_buttonPanel);
        Controls.Add(_progressBar);

        Text = "Creating Account";
        ClientSize = new Size(380, 120);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        CancelButton = _cancelButton;

        _buttonPanel.ResumeLayout(false);
        ResumeLayout(false);
    }
}
