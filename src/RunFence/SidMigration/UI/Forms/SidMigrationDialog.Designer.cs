#nullable disable

using System.ComponentModel;

namespace RunFence.SidMigration.UI.Forms;

partial class SidMigrationDialog
{
    private IContainer components = null;

    private Label _stepTitleLabel;
    private Panel _stepPanel;
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
        _backButton = new Button();
        _nextButton = new Button();
        _cancelCloseButton = new Button();

        SuspendLayout();

        // _stepTitleLabel
        _stepTitleLabel.Location = new Point(15, 10);
        _stepTitleLabel.Size = new Size(570, 25);
        _stepTitleLabel.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);

        // _stepPanel
        _stepPanel.Location = new Point(0, 40);
        _stepPanel.Size = new Size(600, 390);

        // _backButton
        _backButton.Text = "Back";
        _backButton.Location = new Point(340, 440);
        _backButton.Size = new Size(75, 28);
        _backButton.FlatStyle = FlatStyle.System;
        _backButton.Enabled = false;
        _backButton.CausesValidation = false;
        _backButton.Click += OnBackClick;

        // _nextButton
        _nextButton.Text = "Next";
        _nextButton.Location = new Point(425, 440);
        _nextButton.Size = new Size(75, 28);
        _nextButton.FlatStyle = FlatStyle.System;
        _nextButton.Click += OnNextClick;

        // _cancelCloseButton
        _cancelCloseButton.Text = "Cancel";
        _cancelCloseButton.Location = new Point(510, 440);
        _cancelCloseButton.Size = new Size(75, 28);
        _cancelCloseButton.FlatStyle = FlatStyle.System;
        _cancelCloseButton.CausesValidation = false;
        _cancelCloseButton.Click += OnCancelCloseClick;

        // SidMigrationDialog
        Text = "SID Migration Wizard";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(600, 480);
        Controls.AddRange(new Control[] { _stepTitleLabel, _stepPanel, _backButton, _nextButton, _cancelCloseButton });

        ResumeLayout(false);
        PerformLayout();
    }
}
