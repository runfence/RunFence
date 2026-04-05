#nullable disable

using System.ComponentModel;

namespace RunFence.SidMigration.UI.Forms;

partial class SidMigrationInAppStep
{
    private IContainer components = null;

    private Label _summaryLabel;
    private ListBox _listBox;
    private Button _applyButton;
    private Label _resultLabel;

    private SidMigrationInAppStep() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _summaryLabel = new Label();
        _listBox = new ListBox();
        _applyButton = new Button();
        _resultLabel = new Label();

        SuspendLayout();

        // _summaryLabel
        _summaryLabel.Location = new Point(15, 10);
        _summaryLabel.Size = new Size(560, 25);

        // _listBox
        _listBox.Location = new Point(15, 40);
        _listBox.Size = new Size(560, 130);

        // _applyButton
        _applyButton.Text = "Apply";
        _applyButton.Location = new Point(15, 180);
        _applyButton.Size = new Size(100, 28);
        _applyButton.FlatStyle = FlatStyle.System;
        _applyButton.Visible = false;
        _applyButton.Click += OnApplyClick;

        // _resultLabel
        _resultLabel.Location = new Point(15, 220);
        _resultLabel.Size = new Size(560, 100);
        _resultLabel.Visible = false;

        Controls.AddRange(new Control[] { _summaryLabel, _listBox, _applyButton, _resultLabel });

        AutoScaleMode = AutoScaleMode.Font;
        Size = new Size(595, 330);
        ResumeLayout(false);
    }
}
