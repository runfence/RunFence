#nullable disable

using System.ComponentModel;

namespace RunFence.SidMigration.UI.Forms;

partial class SidMigrationPathStep
{
    private IContainer components = null;

    private CheckedListBox _pathListBox;
    private Button _selectAllButton;
    private Button _deselectAllButton;
    private Button _addPathButton;
    private Button _skipButton;
    private Label _hintLabel;

    private SidMigrationPathStep() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _pathListBox = new CheckedListBox();
        _selectAllButton = new Button();
        _deselectAllButton = new Button();
        _addPathButton = new Button();
        _skipButton = new Button();
        _hintLabel = new Label();

        SuspendLayout();

        // _pathListBox
        _pathListBox.Location = new Point(15, 10);
        _pathListBox.Size = new Size(450, 200);
        _pathListBox.CheckOnClick = true;

        // _selectAllButton
        _selectAllButton.Text = "Select All";
        _selectAllButton.Location = new Point(475, 10);
        _selectAllButton.Size = new Size(100, 28);
        _selectAllButton.FlatStyle = FlatStyle.System;
        _selectAllButton.Click += OnSelectAllClick;

        // _deselectAllButton
        _deselectAllButton.Text = "Deselect All";
        _deselectAllButton.Location = new Point(475, 45);
        _deselectAllButton.Size = new Size(100, 28);
        _deselectAllButton.FlatStyle = FlatStyle.System;
        _deselectAllButton.Click += OnDeselectAllClick;

        // _addPathButton
        _addPathButton.Text = "Add Path...";
        _addPathButton.Location = new Point(475, 85);
        _addPathButton.Size = new Size(100, 28);
        _addPathButton.FlatStyle = FlatStyle.System;
        _addPathButton.Click += OnAddPathClick;

        // _skipButton
        _skipButton.Text = "Skip \u2014 I know the SIDs";
        _skipButton.Location = new Point(15, 220);
        _skipButton.Size = new Size(200, 28);
        _skipButton.FlatStyle = FlatStyle.System;
        _skipButton.Click += OnSkipClick;

        // _hintLabel
        _hintLabel.Text = "Click 'Discover' to scan for orphaned SIDs, or 'Skip' to manually enter SID mappings.";
        _hintLabel.Location = new Point(15, 260);
        _hintLabel.Size = new Size(560, 40);

        Controls.AddRange(new Control[] { _pathListBox, _selectAllButton, _deselectAllButton, _addPathButton, _skipButton, _hintLabel });

        AutoScaleMode = AutoScaleMode.Font;
        Size = new Size(595, 310);
        ResumeLayout(false);
    }
}
