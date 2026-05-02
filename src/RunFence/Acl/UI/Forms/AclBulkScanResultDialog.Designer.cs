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
    private DataGridViewCheckBoxColumn colSelect;
    private DataGridViewTextBoxColumn Account;
    private DataGridViewTextBoxColumn Grants;
    private DataGridViewTextBoxColumn Traverse;

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
        colSelect = new DataGridViewCheckBoxColumn();
        Grants = new DataGridViewTextBoxColumn();
        Traverse = new DataGridViewTextBoxColumn();
        Account = new DataGridViewTextBoxColumn();

        ((ISupportInitialize)_grid).BeginInit();
        _buttonPanel.SuspendLayout();
        SuspendLayout();

        // _grid
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Dock = DockStyle.Fill;

        // colSelect
        colSelect.Name = "colSelect";
        colSelect.HeaderText = "Add";
        colSelect.FillWeight = 8;
        colSelect.SortMode = DataGridViewColumnSortMode.NotSortable;

        // Account
        Account.Name = "Account";
        Account.HeaderText = "Account Name";
        Account.FillWeight = 40;
        Account.ReadOnly = true;

        // Grants
        Grants.Name = "Grants";
        Grants.HeaderText = "Grants Found";
        Grants.FillWeight = 26;
        Grants.ReadOnly = true;

        // Traverse
        Traverse.Name = "Traverse";
        Traverse.HeaderText = "Traverse Found";
        Traverse.FillWeight = 26;
        Traverse.ReadOnly = true;

        _grid.Columns.AddRange(new DataGridViewColumn[] { Account, Grants, Traverse, colSelect });
        _grid.CellContentClick += OnGridCellContentClick;

        // _infoLabel
        _infoLabel.Dock = DockStyle.Top;
        _infoLabel.AutoSize = true;
        _infoLabel.TextAlign = ContentAlignment.MiddleLeft;
        _infoLabel.Padding = new Padding(5, 4, 5, 4);
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
