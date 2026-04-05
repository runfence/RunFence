#nullable disable

using System.ComponentModel;
using RunFence.UI.Controls;

namespace RunFence.Apps.UI.Forms;

partial class AppDiscoveryDialog
{
    private IContainer components = null;

    private TextBox _searchTextBox;
    private StyledDataGridView _grid;
    private DataGridViewImageColumn _iconCol;
    private DataGridViewTextBoxColumn _nameCol;
    private DataGridViewTextBoxColumn _targetPathCol;
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
        _iconCol = new DataGridViewImageColumn();
        _nameCol = new DataGridViewTextBoxColumn();
        _targetPathCol = new DataGridViewTextBoxColumn();
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
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        // _iconCol
        _iconCol.Name = "Icon";
        _iconCol.HeaderText = "";
        _iconCol.Width = 24;
        _iconCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _iconCol.Resizable = DataGridViewTriState.False;
        _iconCol.DefaultCellStyle.NullValue = null;
        _iconCol.ImageLayout = DataGridViewImageCellLayout.Zoom;
        _grid.Columns.Add(_iconCol);

        _nameCol.Name = "Name";
        _nameCol.HeaderText = "Name";
        _nameCol.FillWeight = 35;
        _grid.Columns.Add(_nameCol);
        _targetPathCol.Name = "TargetPath";
        _targetPathCol.HeaderText = "Target Path";
        _targetPathCol.FillWeight = 65;
        _grid.Columns.Add(_targetPathCol);
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
