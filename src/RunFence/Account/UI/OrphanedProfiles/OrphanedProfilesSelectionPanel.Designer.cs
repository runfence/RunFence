#nullable disable

using System.ComponentModel;

namespace RunFence.Account.UI.OrphanedProfiles;

partial class OrphanedProfilesSelectionPanel
{
    private IContainer components = null;

    private Label _descLabel;
    private CheckedListBox _profileList;
    private Button _selectAllButton;
    private Button _deselectAllButton;

    public OrphanedProfilesSelectionPanel()
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
        _descLabel = new Label();
        _profileList = new CheckedListBox();
        _selectAllButton = new Button();
        _deselectAllButton = new Button();

        SuspendLayout();

        // _descLabel
        _descLabel.Location = new Point(15, 15);
        _descLabel.Size = new Size(870, 35);
        _descLabel.AutoSize = false;

        // _profileList
        _profileList.Location = new Point(15, 55);
        _profileList.Size = new Size(870, 530);
        _profileList.CheckOnClick = true;
        _profileList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        // _selectAllButton
        _selectAllButton.Text = "Select All";
        _selectAllButton.Location = new Point(15, 595);
        _selectAllButton.Size = new Size(80, 25);
        _selectAllButton.FlatStyle = FlatStyle.System;
        _selectAllButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _selectAllButton.Click += OnSelectAllClick;

        // _deselectAllButton
        _deselectAllButton.Text = "Deselect All";
        _deselectAllButton.Location = new Point(105, 595);
        _deselectAllButton.Size = new Size(95, 25);
        _deselectAllButton.FlatStyle = FlatStyle.System;
        _deselectAllButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _deselectAllButton.Click += OnDeselectAllClick;

        // OrphanedProfilesSelectionPanel
        AutoScaleMode = AutoScaleMode.Inherit;
        Margin = Padding.Empty;
        Controls.AddRange(new Control[] { _descLabel, _profileList, _selectAllButton, _deselectAllButton });

        ResumeLayout(false);
        PerformLayout();
    }
}
