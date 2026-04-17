#nullable disable

using System.ComponentModel;

namespace RunFence.SidMigration.UI.Forms;

partial class SidMigrationDialog
{
    private IContainer components = null;

    private Label _stepTitleLabel;
    private Panel _stepPanel;
    private Panel _buttonPanel;
    private Button _backButton;
    private Button _nextButton;
    private Button _cancelCloseButton;

    private SidMigrationDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _stepTitleLabel = new Label();
        _stepPanel = new Panel();
        _buttonPanel = new Panel();
        _backButton = new Button();
        _nextButton = new Button();
        _cancelCloseButton = new Button();

        SuspendLayout();
        _buttonPanel.SuspendLayout();

        // _stepTitleLabel
        _stepTitleLabel.Dock = DockStyle.Top;
        _stepTitleLabel.Padding = new Padding(12, 8, 12, 8);
        _stepTitleLabel.AutoSize = true;
        _stepTitleLabel.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);

        // _cancelCloseButton
        _cancelCloseButton.Text = "Cancel";
        _cancelCloseButton.Size = new Size(80, 28);
        _cancelCloseButton.Dock = DockStyle.Right;
        _cancelCloseButton.FlatStyle = FlatStyle.System;
        _cancelCloseButton.CausesValidation = false;
        _cancelCloseButton.Click += OnCancelCloseClick;

        // _nextButton
        _nextButton.Text = "Next";
        _nextButton.Size = new Size(80, 28);
        _nextButton.Dock = DockStyle.Right;
        _nextButton.FlatStyle = FlatStyle.System;
        _nextButton.Click += OnNextClick;

        // _backButton
        _backButton.Text = "Back";
        _backButton.Size = new Size(80, 28);
        _backButton.Dock = DockStyle.Right;
        _backButton.FlatStyle = FlatStyle.System;
        _backButton.Enabled = false;
        _backButton.CausesValidation = false;
        _backButton.Click += OnBackClick;

        // _buttonPanel
        _buttonPanel.Dock = DockStyle.Bottom;
        _buttonPanel.Height = 44;
        _buttonPanel.Padding = new Padding(8, 8, 8, 8);
        _buttonPanel.Controls.AddRange(new Control[] { _cancelCloseButton, _nextButton, _backButton });

        // _stepPanel
        _stepPanel.Dock = DockStyle.Fill;

        // SidMigrationDialog
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Text = "SID Migration Wizard";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(600, 480);
        Controls.AddRange(new Control[] { _stepPanel, _buttonPanel, _stepTitleLabel });

        _buttonPanel.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
