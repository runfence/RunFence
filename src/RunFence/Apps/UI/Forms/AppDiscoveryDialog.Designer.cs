#nullable disable

using System.ComponentModel;
using RunFence.UI.Controls;

namespace RunFence.Apps.UI.Forms;

partial class AppDiscoveryDialog
{
    private IContainer components = null;

    private TextBox _searchTextBox;
    private StyledDataGridView _grid;
    private DataGridViewImageColumn colIcon;
    private DataGridViewTextBoxColumn AppName;
    private DataGridViewTextBoxColumn TargetPath;
    private Label _emptyLabel;
    private FlowLayoutPanel _buttonPanel;
    private Button _okButton;
    private Button _cancelButton;

    private AppDiscoveryDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            DisposeIconCache();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _searchTextBox = new TextBox();
        _grid = new StyledDataGridView();
        colIcon = new DataGridViewImageColumn();
        AppName = new DataGridViewTextBoxColumn();
        TargetPath = new DataGridViewTextBoxColumn();
        _emptyLabel = new Label();
        _buttonPanel = new FlowLayoutPanel();
        _okButton = new Button();
        _cancelButton = new Button();

        ((ISupportInitialize)_grid).BeginInit();
        _buttonPanel.SuspendLayout();
        SuspendLayout();

        // _searchTextBox
        _searchTextBox.Dock = DockStyle.Top;
        _searchTextBox.PlaceholderText = "Search applications...";

        // _grid
        _grid.ReadOnly = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        // colIcon
        colIcon.Name = "colIcon";
        colIcon.HeaderText = "";
        colIcon.Width = 24;
        colIcon.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        colIcon.Resizable = DataGridViewTriState.False;
        colIcon.DefaultCellStyle.NullValue = null;
        colIcon.ImageLayout = DataGridViewImageCellLayout.Zoom;
        _grid.Columns.Add(colIcon);

        AppName.Name = "AppName";
        AppName.HeaderText = "Name";
        AppName.FillWeight = 35;
        _grid.Columns.Add(AppName);
        TargetPath.Name = "TargetPath";
        TargetPath.HeaderText = "Target Path";
        TargetPath.FillWeight = 65;
        _grid.Columns.Add(TargetPath);
        _grid.Dock = DockStyle.Fill;
        _grid.CellDoubleClick += OnGridDoubleClick;

        // _emptyLabel
        _emptyLabel.Text = "No applications found";
        _emptyLabel.Dock = DockStyle.Fill;
        _emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
        _emptyLabel.Visible = false;

        // _buttonPanel
        _buttonPanel.Dock = DockStyle.Bottom;
        _buttonPanel.Height = 45;
        _buttonPanel.FlowDirection = FlowDirection.RightToLeft;
        _buttonPanel.Padding = new Padding(5);
        _buttonPanel.Controls.Add(_cancelButton);
        _buttonPanel.Controls.Add(_okButton);

        // _okButton
        _okButton.Text = "OK";
        _okButton.Width = 80;
        _okButton.Height = 28;
        _okButton.FlatStyle = FlatStyle.System;
        _okButton.Click += OnOkClick;

        // _cancelButton
        _cancelButton.Text = "Cancel";
        _cancelButton.Width = 80;
        _cancelButton.Height = 28;
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.FlatStyle = FlatStyle.System;

        // AppDiscoveryDialog
        Text = "Discover Applications";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(650, 500);
        MinimumSize = new Size(500, 350);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        Controls.Add(_grid);
        Controls.Add(_emptyLabel);
        Controls.Add(_searchTextBox);
        Controls.Add(_buttonPanel);

        ((ISupportInitialize)_grid).EndInit();
        _buttonPanel.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
