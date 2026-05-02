#nullable disable

using System.ComponentModel;

namespace RunFence.SidMigration.UI.Forms;

partial class SidMigrationDialog
{
    private IContainer components = null;

    private Label _stepTitleLabel;
    private Panel _stepPanel;
    private Panel _buttonPanel;
    private Button _secondaryButton;
    private Button _nextButton;

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
        _secondaryButton = new Button();
        _nextButton = new Button();

        SuspendLayout();
        _buttonPanel.SuspendLayout();

        // _stepTitleLabel
        _stepTitleLabel.Dock = DockStyle.Top;
        _stepTitleLabel.Padding = new Padding(12, 8, 12, 8);
        _stepTitleLabel.AutoSize = true;
        _stepTitleLabel.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);

        // _nextButton
        _nextButton.Text = "Next";
        _nextButton.Size = new Size(80, 28);
        _nextButton.Dock = DockStyle.Right;
        _nextButton.FlatStyle = FlatStyle.System;
        _nextButton.Click += OnNextClick;

        // _secondaryButton
        _secondaryButton.Text = "Cancel";
        _secondaryButton.Size = new Size(80, 28);
        _secondaryButton.Dock = DockStyle.Right;
        _secondaryButton.FlatStyle = FlatStyle.System;
        _secondaryButton.CausesValidation = false;
        _secondaryButton.Click += OnSecondaryButtonClick;

        // _buttonPanel
        _buttonPanel.Dock = DockStyle.Bottom;
        _buttonPanel.Height = 44;
        _buttonPanel.Padding = new Padding(8, 8, 8, 8);
        _buttonPanel.Controls.AddRange(new Control[] { _secondaryButton, _nextButton });

        // _stepPanel
        _stepPanel.Dock = DockStyle.Fill;

        // SidMigrationDialog
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Text = "SID Migration Wizard";
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(600, 480);
        ClientSize = new Size(600, 480);
        CancelButton = _secondaryButton;
        Controls.AddRange(new Control[] { _stepPanel, _buttonPanel, _stepTitleLabel });

        _buttonPanel.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
