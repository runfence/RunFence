#nullable disable

using System.ComponentModel;
using RunFence.UI.Controls;

namespace RunFence.Apps.UI.Forms;

partial class HandlerMappingsDialog
{
    private IContainer components = null;

    private ToolStrip _toolbar;
    private ToolStripButton _addButton;
    private ToolStripButton _editButton;
    private ToolStripButton _removeButton;
    private ToolStripButton _reapplyButton;
    private ToolStripButton _openDefaultAppsButton;
    private StyledDataGridView _grid;
    private DataGridViewTextBoxColumn _colKey;
    private DataGridViewTextBoxColumn _colAppName;
    private DataGridViewTextBoxColumn _colAccount;
    private DataGridViewTextBoxColumn _colArgsTemplate;
    private ContextMenuStrip _contextMenu;
    private ToolStripMenuItem _ctxAdd;
    private ToolStripMenuItem _ctxEdit;
    private ToolStripMenuItem _ctxRemove;
    private Label _warningLabel;

    private HandlerMappingsDialog() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _toolbar = new ToolStrip();
        _addButton = new ToolStripButton();
        _editButton = new ToolStripButton();
        _removeButton = new ToolStripButton();
        _reapplyButton = new ToolStripButton();
        _openDefaultAppsButton = new ToolStripButton();
        _grid = new StyledDataGridView();
        _colKey = new DataGridViewTextBoxColumn();
        _colAppName = new DataGridViewTextBoxColumn();
        _colAccount = new DataGridViewTextBoxColumn();
        _colArgsTemplate = new DataGridViewTextBoxColumn();
        _contextMenu = new ContextMenuStrip();
        _ctxAdd = new ToolStripMenuItem();
        _ctxEdit = new ToolStripMenuItem();
        _ctxRemove = new ToolStripMenuItem();
        _warningLabel = new Label();

        _toolbar.SuspendLayout();
        ((ISupportInitialize)_grid).BeginInit();
        _contextMenu.SuspendLayout();
        SuspendLayout();

        // Form
        Text = "Associations";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(550, 400);
        MinimumSize = new Size(400, 300);
        FormClosing += OnFormClosing;
        SizeChanged += OnDialogSizeChanged;

        // _toolbar
        _toolbar.Dock = DockStyle.Top;
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.RenderMode = ToolStripRenderMode.System;
        _toolbar.ImageScalingSize = new Size(24, 24);
        _toolbar.Items.AddRange(new ToolStripItem[] { _addButton, _editButton, _removeButton, _reapplyButton, _openDefaultAppsButton });

        // _addButton
        _addButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _addButton.ToolTipText = "Add association...";
        _addButton.Click += OnAddClick;

        // _editButton
        _editButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _editButton.ToolTipText = "Change application for selected association";
        _editButton.Enabled = false;
        _editButton.Click += OnChangeAppClick;

        // _removeButton
        _removeButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _removeButton.ToolTipText = "Remove association";
        _removeButton.Enabled = false;
        _removeButton.Click += OnRemoveClick;

        // _reapplyButton
        _reapplyButton.Text = "Reapply";
        _reapplyButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _reapplyButton.Alignment = ToolStripItemAlignment.Right;
        _reapplyButton.Click += OnReapplyClick;

        // _openDefaultAppsButton
        _openDefaultAppsButton.Text = "Open Default Apps";
        _openDefaultAppsButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _openDefaultAppsButton.Alignment = ToolStripItemAlignment.Right;
        _openDefaultAppsButton.Click += OnOpenDefaultAppsClick;

        // _grid
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.ContextMenuStrip = _contextMenu;
        _grid.Columns.AddRange(new DataGridViewColumn[] { _colKey, _colAppName, _colAccount, _colArgsTemplate });
        _grid.SelectionChanged += OnGridSelectionChanged;
        _grid.MouseDown += OnGridMouseDown;
        _grid.KeyDown += OnGridKeyDown;
        _grid.CellDoubleClick += OnGridCellDoubleClick;

        // _colKey
        _colKey.HeaderText = "Extension / Protocol";
        _colKey.Name = "colKey";
        _colKey.Width = 160;
        _colKey.ReadOnly = true;

        // _colAppName
        _colAppName.HeaderText = "Application";
        _colAppName.Name = "colAppName";
        _colAppName.Width = 200;
        _colAppName.ReadOnly = true;

        // _colAccount
        _colAccount.HeaderText = "Account";
        _colAccount.Name = "colAccount";
        _colAccount.Width = 130;
        _colAccount.ReadOnly = true;

        // _colArgsTemplate
        _colArgsTemplate.HeaderText = "Args Template";
        _colArgsTemplate.Name = "colArgsTemplate";
        _colArgsTemplate.ReadOnly = true;
        _colArgsTemplate.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        // _contextMenu
        _contextMenu.Items.AddRange(new ToolStripItem[] { _ctxAdd, _ctxEdit, _ctxRemove });
        _contextMenu.Opening += OnContextMenuOpening;

        // _ctxAdd
        _ctxAdd.Text = "Add...";
        _ctxAdd.Click += OnAddClick;

        // _ctxEdit
        _ctxEdit.Text = "Change Application...";
        _ctxEdit.Enabled = false;
        _ctxEdit.Click += OnChangeAppClick;

        // _ctxRemove
        _ctxRemove.Text = "Remove";
        _ctxRemove.Click += OnRemoveClick;

        // _warningLabel
        _warningLabel.Dock = DockStyle.Bottom;
        _warningLabel.Padding = new Padding(5, 4, 5, 4);
        _warningLabel.Text = "Many common associations will require logging on into account, opening Default Apps and applying RunFence.";
        _warningLabel.ForeColor = SystemColors.GrayText;

        // Add controls
        Controls.Add(_grid);
        Controls.Add(_warningLabel);
        Controls.Add(_toolbar);

        _toolbar.ResumeLayout(false);
        _toolbar.PerformLayout();
        ((ISupportInitialize)_grid).EndInit();
        _contextMenu.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
