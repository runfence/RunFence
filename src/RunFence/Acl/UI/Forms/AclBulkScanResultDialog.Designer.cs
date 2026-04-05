#nullable disable

using System.ComponentModel;
using RunFence.UI.Controls;

namespace RunFence.Acl.UI.Forms;

partial class AclBulkScanResultDialog
{
    private IContainer components = null;

    private StyledDataGridView _grid;
    private FlowLayoutPanel _buttonPanel;
    private Button _okButton;
    private Button _cancelButton;
    private Label _infoLabel;
    private DataGridViewCheckBoxColumn _selectCol;
    private DataGridViewTextBoxColumn _accountCol;
    private DataGridViewTextBoxColumn _grantsCol;
    private DataGridViewTextBoxColumn _traverseCol;

    private AclBulkScanResultDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _grid = new StyledDataGridView();
        _buttonPanel = new FlowLayoutPanel();
        _okButton = new Button();
        _cancelButton = new Button();
        _infoLabel = new Label();
        _selectCol = new DataGridViewCheckBoxColumn();
        _grantsCol = new DataGridViewTextBoxColumn();
        _traverseCol = new DataGridViewTextBoxColumn();
        _accountCol = new DataGridViewTextBoxColumn();

        ((ISupportInitialize)_grid).BeginInit();
        _buttonPanel.SuspendLayout();
        SuspendLayout();

        // _grid
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Dock = DockStyle.Fill;

        // _selectCol
        _selectCol.Name = "Select";
        _selectCol.HeaderText = "Add";
        _selectCol.FillWeight = 8;
        _selectCol.SortMode = DataGridViewColumnSortMode.NotSortable;

        // _accountCol
        _accountCol.Name = "Account";
        _accountCol.HeaderText = "Account Name";
        _accountCol.FillWeight = 40;
        _accountCol.ReadOnly = true;

        // _grantsCol
        _grantsCol.Name = "Grants";
        _grantsCol.HeaderText = "Grants Found";
        _grantsCol.FillWeight = 26;
        _grantsCol.ReadOnly = true;

        // _traverseCol
        _traverseCol.Name = "Traverse";
        _traverseCol.HeaderText = "Traverse Found";
        _traverseCol.FillWeight = 26;
        _traverseCol.ReadOnly = true;

        _grid.Columns.AddRange(new DataGridViewColumn[] { _accountCol, _grantsCol, _traverseCol, _selectCol });
        _grid.CellContentClick += OnGridCellContentClick;

        // _infoLabel
        _infoLabel.Dock = DockStyle.Top;
        _infoLabel.Height = 28;
        _infoLabel.TextAlign = ContentAlignment.MiddleLeft;
        _infoLabel.Padding = new Padding(5, 0, 5, 0);
        _infoLabel.Text = "Select accounts to import ACL entries from the scan results:";

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
        _cancelButton.FlatStyle = FlatStyle.System;
        _cancelButton.DialogResult = DialogResult.Cancel;

        // AclBulkScanResultDialog
        Text = "Scan ACLs - Results";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(600, 400);
        MinimumSize = new Size(500, 300);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        Controls.Add(_grid);
        Controls.Add(_infoLabel);
        Controls.Add(_buttonPanel);

        ((ISupportInitialize)_grid).EndInit();
        _buttonPanel.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
