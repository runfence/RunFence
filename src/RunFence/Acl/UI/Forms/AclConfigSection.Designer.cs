#nullable disable

using System.ComponentModel;
using RunFence.UI.Controls;

namespace RunFence.Acl.UI.Forms;

partial class AclConfigSection
{
    private IContainer components = null;

    private CheckBox _restrictAclCheckBox;
    private Panel _aclModePanel;
    private RadioButton _aclModeDenyRadio;
    private RadioButton _aclModeAllowRadio;
    private Label _aclSeparator;
    private Panel _aclTargetPanel;
    private RadioButton _aclFileRadio;
    private RadioButton _aclFolderRadio;
    private ComboBox _folderDepthComboBox;
    private Label _aclPathLabel;
    private Label _deniedRightsLabel;
    private ComboBox _deniedRightsComboBox;
    private Panel _allowPanel;
    private ToolStrip _allowToolStrip;
    private ToolStripButton _allowTsAddButton;
    private ToolStripButton _allowTsRemoveButton;
    private StyledDataGridView _allowEntriesGrid;
    private DataGridViewTextBoxColumn _allowAccountCol;
    private DataGridViewCheckBoxColumn _allowExecuteCol;
    private DataGridViewCheckBoxColumn _allowWriteCol;
    private ContextMenuStrip _allowContextMenu;
    private ToolStripMenuItem _allowCtxAdd;
    private ToolStripMenuItem _allowCtxRemove;
    private Label _allowConflictLabel;

    private AclConfigSection() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _restrictAclCheckBox = new CheckBox();
        _aclModePanel = new Panel();
        _aclModeDenyRadio = new RadioButton();
        _aclModeAllowRadio = new RadioButton();
        _aclSeparator = new Label();
        _aclTargetPanel = new Panel();
        _aclFileRadio = new RadioButton();
        _aclFolderRadio = new RadioButton();
        _folderDepthComboBox = new ComboBox();
        _aclPathLabel = new Label();
        _deniedRightsLabel = new Label();
        _deniedRightsComboBox = new ComboBox();
        _allowPanel = new Panel();
        _allowToolStrip = new ToolStrip();
        _allowTsAddButton = new ToolStripButton();
        _allowTsRemoveButton = new ToolStripButton();
        _allowEntriesGrid = new StyledDataGridView();
        _allowAccountCol = new DataGridViewTextBoxColumn();
        _allowExecuteCol = new DataGridViewCheckBoxColumn();
        _allowWriteCol = new DataGridViewCheckBoxColumn();
        _allowContextMenu = new ContextMenuStrip();
        _allowCtxAdd = new ToolStripMenuItem();
        _allowCtxRemove = new ToolStripMenuItem();
        _allowConflictLabel = new Label();

        _aclModePanel.SuspendLayout();
        _aclTargetPanel.SuspendLayout();
        _allowPanel.SuspendLayout();
        _allowToolStrip.SuspendLayout();
        ((ISupportInitialize)_allowEntriesGrid).BeginInit();
        _allowContextMenu.SuspendLayout();
        SuspendLayout();

        // _restrictAclCheckBox
        _restrictAclCheckBox.Text = "Restrict access (manage ACLs)";
        _restrictAclCheckBox.Location = new Point(15, 5);
        _restrictAclCheckBox.AutoSize = true;
        _restrictAclCheckBox.Checked = true;
        _restrictAclCheckBox.CheckedChanged += OnRestrictAclCheckedChanged;

        // _aclModePanel
        _aclModePanel.Location = new Point(15, 30);
        _aclModePanel.Size = new Size(470, 50);
        _aclModePanel.Controls.AddRange(new Control[] { _aclModeDenyRadio, _aclModeAllowRadio });

        // _aclModeDenyRadio
        _aclModeDenyRadio.Text = "Deny mode \u2014 deny other accounts";
        _aclModeDenyRadio.Location = new Point(0, 0);
        _aclModeDenyRadio.AutoSize = true;
        _aclModeDenyRadio.Checked = true;
        _aclModeDenyRadio.CheckedChanged += OnAclModeDenyRadioCheckedChanged;

        // _aclModeAllowRadio
        _aclModeAllowRadio.Text = "Allow mode \u2014 explicit allowlist (breaks inheritance)";
        _aclModeAllowRadio.Location = new Point(0, 25);
        _aclModeAllowRadio.AutoSize = true;

        // _aclSeparator
        _aclSeparator.BorderStyle = BorderStyle.Fixed3D;
        _aclSeparator.Location = new Point(15, 78);
        _aclSeparator.Size = new Size(470, 2);

        // _aclTargetPanel
        _aclTargetPanel.Location = new Point(15, 85);
        _aclTargetPanel.Size = new Size(470, 25);
        _aclTargetPanel.Controls.AddRange(new Control[] { _aclFileRadio, _aclFolderRadio });

        // _aclFileRadio
        _aclFileRadio.Text = "File only";
        _aclFileRadio.Location = new Point(0, 0);
        _aclFileRadio.AutoSize = true;

        // _aclFolderRadio
        _aclFolderRadio.Text = "Folder (with inheritance):";
        _aclFolderRadio.Location = new Point(130, 0);
        _aclFolderRadio.AutoSize = true;
        _aclFolderRadio.Checked = true;
        _aclFolderRadio.CheckedChanged += OnAclFolderRadioCheckedChanged;

        // _folderDepthComboBox
        _folderDepthComboBox.Location = new Point(55, 110);
        _folderDepthComboBox.Size = new Size(425, 23);
        _folderDepthComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _folderDepthComboBox.SelectedIndexChanged += OnFolderDepthSelectedIndexChanged;

        // _aclPathLabel
        _aclPathLabel.Location = new Point(35, 135);
        _aclPathLabel.Size = new Size(445, 20);
        _aclPathLabel.ForeColor = Color.DarkBlue;
        _aclPathLabel.Font = new Font(DefaultFont.FontFamily, 8f);

        // _deniedRightsLabel
        _deniedRightsLabel.Text = "Denied rights:";
        _deniedRightsLabel.Location = new Point(35, 157);
        _deniedRightsLabel.AutoSize = true;

        // _deniedRightsComboBox
        _deniedRightsComboBox.Location = new Point(140, 155);
        _deniedRightsComboBox.Size = new Size(340, 23);
        _deniedRightsComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _deniedRightsComboBox.Items.AddRange(new object[] { "Deny execute", "Deny execute + write", "Deny execute + read + write" });
        _deniedRightsComboBox.SelectedIndex = 0;

        // _allowPanel (contains toolbar + grid for allow-mode entries)
        _allowPanel.Location = new Point(10, 157);
        _allowPanel.Size = new Size(480, 175);
        _allowPanel.Visible = false;
        _allowPanel.Controls.Add(_allowEntriesGrid);
        _allowPanel.Controls.Add(_allowToolStrip);

        // _allowToolStrip
        _allowToolStrip.Dock = DockStyle.Top;
        _allowToolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _allowToolStrip.RenderMode = ToolStripRenderMode.System;
        _allowToolStrip.ImageScalingSize = new Size(24, 24);
        _allowToolStrip.Items.AddRange(new ToolStripItem[] { _allowTsAddButton, _allowTsRemoveButton });

        // _allowTsAddButton
        _allowTsAddButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _allowTsAddButton.ToolTipText = "Add account...";
        _allowTsAddButton.Click += OnAllowAddClick;

        // _allowTsRemoveButton
        _allowTsRemoveButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _allowTsRemoveButton.ToolTipText = "Remove account";
        _allowTsRemoveButton.Enabled = false;
        _allowTsRemoveButton.Click += OnAllowRemoveClick;

        // _allowEntriesGrid
        _allowEntriesGrid.Dock = DockStyle.Fill;
        _allowEntriesGrid.AllowUserToAddRows = false;
        _allowEntriesGrid.AllowUserToDeleteRows = false;
        _allowEntriesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _allowEntriesGrid.MultiSelect = false;
        _allowEntriesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _allowEntriesGrid.ContextMenuStrip = _allowContextMenu;
        _allowAccountCol.Name = "Account";
        _allowAccountCol.HeaderText = "Account";
        _allowAccountCol.ReadOnly = true;
        _allowAccountCol.FillWeight = 60;
        _allowEntriesGrid.Columns.Add(_allowAccountCol);
        _allowExecuteCol.Name = "Execute";
        _allowExecuteCol.HeaderText = "Execute";
        _allowExecuteCol.FillWeight = 20;
        _allowEntriesGrid.Columns.Add(_allowExecuteCol);
        _allowWriteCol.Name = "Write";
        _allowWriteCol.HeaderText = "Write";
        _allowWriteCol.FillWeight = 20;
        _allowEntriesGrid.Columns.Add(_allowWriteCol);
        _allowEntriesGrid.SelectionChanged += OnAllowSelectionChanged;
        _allowEntriesGrid.KeyDown += OnAllowKeyDown;
        _allowEntriesGrid.MouseDown += OnAllowGridMouseDown;
        _allowEntriesGrid.CellValueChanged += OnAllowGridCellValueChanged;
        _allowEntriesGrid.CurrentCellDirtyStateChanged += OnAllowGridCurrentCellDirtyStateChanged;

        // _allowContextMenu
        _allowContextMenu.Items.AddRange(new ToolStripItem[] { _allowCtxAdd, _allowCtxRemove });
        _allowContextMenu.Opening += OnAllowContextMenuOpening;

        // _allowCtxAdd
        _allowCtxAdd.Text = "Add...";
        _allowCtxAdd.Click += OnAllowAddClick;

        // _allowCtxRemove
        _allowCtxRemove.Text = "Remove";
        _allowCtxRemove.Click += OnAllowRemoveClick;

        // _allowConflictLabel
        _allowConflictLabel.Location = new Point(10, 340);
        _allowConflictLabel.Size = new Size(480, 20);
        _allowConflictLabel.ForeColor = Color.Red;
        _allowConflictLabel.Visible = false;

        // AclConfigSection
        AutoScaleMode = AutoScaleMode.Inherit;
        Margin = Padding.Empty;
        Size = new Size(500, 140);
        Controls.AddRange(new Control[]
        {
            _restrictAclCheckBox, _aclModePanel, _aclSeparator, _aclTargetPanel,
            _folderDepthComboBox, _aclPathLabel, _deniedRightsLabel, _deniedRightsComboBox,
            _allowPanel, _allowConflictLabel
        });

        _aclModePanel.ResumeLayout(false);
        _aclModePanel.PerformLayout();
        _aclTargetPanel.ResumeLayout(false);
        _aclTargetPanel.PerformLayout();
        _allowPanel.ResumeLayout(false);
        _allowToolStrip.ResumeLayout(false);
        _allowToolStrip.PerformLayout();
        ((ISupportInitialize)_allowEntriesGrid).EndInit();
        _allowContextMenu.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
