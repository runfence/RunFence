#nullable disable

using System.ComponentModel;

namespace RunFence.Security.UI.Forms;

partial class DisableBlockingDialog
{
    private Label _questionLabel;
    private Button _untilRestartButton;
    private Button _forTenMinutesButton;
    private Button _permanentlyButton;
    private Button _cancelButton;
    private Panel _buttonsPanel;
    private IContainer components = null;

    public DisableBlockingDialog()
    {
        InitializeComponent();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _questionLabel = new Label();
        _untilRestartButton = new Button();
        _forTenMinutesButton = new Button();
        _permanentlyButton = new Button();
        _cancelButton = new Button();
        _buttonsPanel = new Panel();

        SuspendLayout();
        _buttonsPanel.SuspendLayout();

        // _questionLabel
        _questionLabel.Text = "Disable input injection blocking?";
        _questionLabel.Dock = DockStyle.Top;
        _questionLabel.Padding = new Padding(12, 14, 12, 0);
        _questionLabel.AutoSize = false;
        _questionLabel.Height = 42;

        // _buttonsPanel
        _buttonsPanel.Dock = DockStyle.Bottom;
        _buttonsPanel.Height = 44;
        _buttonsPanel.Padding = new Padding(8, 8, 8, 8);
        _buttonsPanel.Controls.Add(_cancelButton);
        _buttonsPanel.Controls.Add(_untilRestartButton);
        _buttonsPanel.Controls.Add(_forTenMinutesButton);
        _buttonsPanel.Controls.Add(_permanentlyButton);

        // _untilRestartButton
        _untilRestartButton.Text = "Until Restart";
        _untilRestartButton.Dock = DockStyle.Left;
        _untilRestartButton.Size = new Size(110, 28);
        _untilRestartButton.FlatStyle = FlatStyle.System;
        _untilRestartButton.Click += OnUntilRestartClick;

        // _forTenMinutesButton
        _forTenMinutesButton.Text = "For 10 Minutes";
        _forTenMinutesButton.Dock = DockStyle.Left;
        _forTenMinutesButton.Size = new Size(120, 28);
        _forTenMinutesButton.FlatStyle = FlatStyle.System;
        _forTenMinutesButton.Click += OnForTenMinutesClick;

        // _permanentlyButton
        _permanentlyButton.Text = "Permanently";
        _permanentlyButton.Dock = DockStyle.Left;
        _permanentlyButton.Size = new Size(110, 28);
        _permanentlyButton.FlatStyle = FlatStyle.System;
        _permanentlyButton.Click += OnPermanentlyClick;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Dock = DockStyle.Right;
        _cancelButton.Size = new Size(80, 28);
        _cancelButton.FlatStyle = FlatStyle.System;

        // DisableBlockingDialog
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Text = "Block Input Injection";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(450, 90);
        CancelButton = _cancelButton;
        Controls.Add(_buttonsPanel);
        Controls.Add(_questionLabel);

        _buttonsPanel.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
