#nullable disable

using System.ComponentModel;

namespace RunFence.SidMigration.UI.Forms;

partial class SidMigrationInAppStep
{
    private IContainer components = null;

    private Label _descriptionLabel;
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
        _descriptionLabel = new Label();
        _summaryLabel = new Label();
        _listBox = new ListBox();
        _applyButton = new Button();
        _resultLabel = new Label();

        SuspendLayout();
        Size = new Size(595, 330);

        // _descriptionLabel
        _descriptionLabel.Location = new Point(15, 10);
        _descriptionLabel.Size = new Size(560, 48);
        _descriptionLabel.Text = "The disk pass is finished, but saved application data can still point to the old security identity until this step runs. Apply this step to rewrite stored references so launch rules, permission records, and account links match the new owner or the removal you chose. Use it whenever the disk phase changed anything, otherwise the interface can stay internally inconsistent.";
        _descriptionLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        // _summaryLabel
        _summaryLabel.Location = new Point(15, 60);
        _summaryLabel.Size = new Size(560, 25);
        _summaryLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        // _listBox
        _listBox.Location = new Point(15, 90);
        _listBox.Size = new Size(560, 110);
        _listBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

        // _applyButton
        _applyButton.Text = "Apply";
        _applyButton.Location = new Point(15, 210);
        _applyButton.Size = new Size(100, 28);
        _applyButton.FlatStyle = FlatStyle.System;
        _applyButton.Visible = false;
        _applyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _applyButton.Click += OnApplyClick;

        // _resultLabel
        _resultLabel.Location = new Point(15, 245);
        _resultLabel.Size = new Size(560, 75);
        _resultLabel.Visible = false;
        _resultLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        Controls.AddRange(new Control[] { _descriptionLabel, _summaryLabel, _listBox, _applyButton, _resultLabel });

        AutoScaleMode = AutoScaleMode.Font;
        ResumeLayout(false);
    }
}
