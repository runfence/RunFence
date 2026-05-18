#nullable disable

using System.ComponentModel;

namespace RunFence.Account.UI.OrphanedProfiles;

partial class OrphanedProfilesSelectionPanel
{
    private IContainer components = null;

    private Label _descLabel;
    private ListView _profileListView;
    private ColumnHeader _pathHeader;
    private ColumnHeader _sizeHeader;
    private Button _selectAllButton;
    private Button _deselectAllButton;

    private OrphanedProfilesSelectionPanel() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sizeCalculationCts?.Cancel();
            _sizeCalculationCts?.Dispose();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _descLabel = new Label();
        _profileListView = new ListView();
        _pathHeader = new ColumnHeader();
        _sizeHeader = new ColumnHeader();
        _selectAllButton = new Button();
        _deselectAllButton = new Button();

        SuspendLayout();
        Size = new Size(900, 630);

        // _descLabel
        _descLabel.Location = new Point(15, 15);
        _descLabel.Size = new Size(870, 35);
        _descLabel.AutoSize = false;

        // _profileListView
        _profileListView.Location = new Point(15, 55);
        _profileListView.Size = new Size(870, 530);
        _profileListView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _profileListView.CheckBoxes = true;
        _profileListView.FullRowSelect = true;
        _profileListView.GridLines = true;
        _profileListView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _profileListView.View = View.Details;
        _profileListView.UseCompatibleStateImageBehavior = false;

        // _pathHeader
        _pathHeader.Text = "Profile Path";
        _pathHeader.Width = 700;

        // _sizeHeader
        _sizeHeader.Text = "Size (MB)";
        _sizeHeader.Width = 140;

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
        _profileListView.Columns.AddRange(new ColumnHeader[] { _pathHeader, _sizeHeader });
        Controls.AddRange(new Control[] { _descLabel, _profileListView, _selectAllButton, _deselectAllButton });

        ResumeLayout(false);
        PerformLayout();
    }
}
