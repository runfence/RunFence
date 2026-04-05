#nullable disable

using System.ComponentModel;

namespace RunFence.Wizard.UI.Forms;

public partial class WizardDialog
{
    private IContainer components = null;

    private Panel _headerPanel;
    private Label _titleLabel;
    private Panel _stepIndicatorPanel;
    private Panel _contentPanel;
    private Panel _footerPanel;
    private Panel _progressPanel;
    private ProgressBar _progressBar;
    private Label _statusLabel;
    private Label _errorLabel;
    private Button _backButton;
    private Button _nextButton;
    private Button _cancelButton;

    private WizardDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _headerPanel = new Panel();
        _titleLabel = new Label();
        _stepIndicatorPanel = new Panel();
        _contentPanel = new Panel();
        _footerPanel = new Panel();
        _progressPanel = new Panel();
        _progressBar = new ProgressBar();
        _statusLabel = new Label();
        _errorLabel = new Label();
        _backButton = new Button();
        _nextButton = new Button();
        _cancelButton = new Button();

        _headerPanel.SuspendLayout();
        _footerPanel.SuspendLayout();
        _progressPanel.SuspendLayout();
        SuspendLayout();

        // _headerPanel
        _headerPanel.Dock = DockStyle.Top;
        _headerPanel.Height = 64;
        _headerPanel.Padding = new Padding(20, 12, 20, 8);
        _headerPanel.Controls.Add(_titleLabel);
        _headerPanel.Controls.Add(_stepIndicatorPanel);

        // _titleLabel
        _titleLabel.AutoSize = true;
        _titleLabel.Location = new Point(20, 12);
        _titleLabel.Text = "Setup Wizard";

        // _stepIndicatorPanel — painted in OnPaint override in main .cs
        _stepIndicatorPanel.Height = 16;
        _stepIndicatorPanel.Dock = DockStyle.Bottom;
        _stepIndicatorPanel.Paint += OnStepIndicatorPaint;

        // _contentPanel
        _contentPanel.Dock = DockStyle.Fill;
        _contentPanel.Padding = new Padding(20, 12, 20, 0);

        // _progressPanel
        _progressPanel.Dock = DockStyle.Top;
        _progressPanel.Height = 52;
        _progressPanel.Padding = new Padding(0, 4, 0, 0);
        _progressPanel.Visible = false;
        _progressPanel.Controls.AddRange(new Control[] { _progressBar, _statusLabel });

        // _progressBar
        _progressBar.Dock = DockStyle.Top;
        _progressBar.Height = 6;
        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = 30;

        // _statusLabel
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.Padding = new Padding(0, 4, 0, 0);
        _statusLabel.Text = "Please wait...";

        // _errorLabel
        _errorLabel.Dock = DockStyle.Bottom;
        _errorLabel.Height = 22;
        _errorLabel.TextAlign = ContentAlignment.MiddleLeft;
        _errorLabel.Padding = new Padding(20, 0, 20, 0);
        _errorLabel.Visible = false;

        // _footerPanel
        _footerPanel.Dock = DockStyle.Bottom;
        _footerPanel.Height = 48;
        _footerPanel.Padding = new Padding(16, 10, 16, 10);
        // Order: Cancel (Dock=Left), Back (Dock=Right), Next (Dock=Right, last = highest z-order = rightmost)
        _footerPanel.Controls.AddRange(new Control[] { _cancelButton, _backButton, _nextButton });

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.Size = new Size(88, 28);
        _cancelButton.FlatStyle = FlatStyle.System;
        _cancelButton.Dock = DockStyle.Left;
        _cancelButton.Click += OnCancelClick;

        // _backButton
        _backButton.Text = "\u2190 Back";
        _backButton.Size = new Size(88, 28);
        _backButton.FlatStyle = FlatStyle.System;
        _backButton.Enabled = false;
        _backButton.Dock = DockStyle.Right;
        _backButton.Click += OnBackClick;

        // _nextButton
        _nextButton.Text = "Next \u2192";
        _nextButton.Size = new Size(104, 28);
        _nextButton.FlatStyle = FlatStyle.System;
        _nextButton.Dock = DockStyle.Right;
        _nextButton.Click += OnNextClick;

        // WizardDialog
        Text = "RunFence Setup Wizard";
        ClientSize = new Size(600, 500);
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AcceptButton = _nextButton;
        CancelButton = _cancelButton;
        FormClosing += OnFormClosing;

        Controls.Add(_contentPanel);
        Controls.Add(_progressPanel);
        Controls.Add(_errorLabel);
        Controls.Add(_footerPanel);
        Controls.Add(_headerPanel);

        _headerPanel.ResumeLayout(false);
        _headerPanel.PerformLayout();
        _footerPanel.ResumeLayout(false);
        _progressPanel.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
