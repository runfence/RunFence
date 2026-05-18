#nullable disable

using System.ComponentModel;
using RunFence.UI.Controls;
using RunFence.UI.Forms;

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
    private DataGridViewTextBoxColumn colKey;
    private DataGridViewTextBoxColumn colAppName;
    private DataGridViewTextBoxColumn colAccount;
    private DataGridViewTextBoxColumn colArgsTemplate;
    private ContextMenuStrip _contextMenu;
    private ToolStripMenuItem _ctxAdd;
    private ToolStripMenuItem _ctxEdit;
    private ToolStripMenuItem _ctxRemove;
    private Label _warningLabel;
    private Panel _topRowHost;
    private Panel _contextHelpHost;
    private ContextHelpButton _contextHelpButton;

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
        colKey = new DataGridViewTextBoxColumn();
        colAppName = new DataGridViewTextBoxColumn();
        colAccount = new DataGridViewTextBoxColumn();
        colArgsTemplate = new DataGridViewTextBoxColumn();
        _contextMenu = new ContextMenuStrip();
        _ctxAdd = new ToolStripMenuItem();
        _ctxEdit = new ToolStripMenuItem();
        _ctxRemove = new ToolStripMenuItem();
        _warningLabel = new Label();
        _topRowHost = new Panel();
        _contextHelpHost = new Panel();
        _contextHelpButton = new ContextHelpButton();

        _topRowHost.SuspendLayout();
        ((ISupportInitialize)_grid).BeginInit();
        _contextMenu.SuspendLayout();
        _contextHelpHost.SuspendLayout();
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
        _toolbar.AutoSize = false;
        _toolbar.Dock = DockStyle.Fill;
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.Height = 29;
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
        _grid.ReadOnly = true;
        _grid.ContextMenuStrip = _contextMenu;
        _grid.Columns.AddRange(new DataGridViewColumn[] { colKey, colAppName, colAccount, colArgsTemplate });
        _grid.SelectionChanged += OnGridSelectionChanged;
        _grid.MouseDown += OnGridMouseDown;
        _grid.KeyDown += OnGridKeyDown;
        _grid.CellDoubleClick += OnGridCellDoubleClick;

        // colKey
        colKey.HeaderText = "Extension / Protocol";
        colKey.Name = "colKey";
        colKey.Width = 160;
        colKey.ReadOnly = true;

        // colAppName
        colAppName.HeaderText = "Application";
        colAppName.Name = "colAppName";
        colAppName.Width = 200;
        colAppName.ReadOnly = true;

        // colAccount
        colAccount.HeaderText = "Account";
        colAccount.Name = "colAccount";
        colAccount.Width = 130;
        colAccount.ReadOnly = true;

        // colArgsTemplate
        colArgsTemplate.HeaderText = "Args Template";
        colArgsTemplate.Name = "colArgsTemplate";
        colArgsTemplate.ReadOnly = true;
        colArgsTemplate.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

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

        // _topRowHost
        _topRowHost.BackColor = SystemColors.Control;
        _topRowHost.Dock = DockStyle.Top;
        _topRowHost.Height = 33;
        _topRowHost.Padding = new Padding(0, 2, 0, 2);
        _topRowHost.TabStop = false;

        // _contextHelpHost
        _contextHelpHost.BackColor = SystemColors.Control;
        _contextHelpHost.Dock = DockStyle.Right;
        _contextHelpHost.Padding = Padding.Empty;
        _contextHelpHost.Size = new Size(29, 29);
        _contextHelpHost.TabStop = false;

        // _contextHelpButton
        _contextHelpButton.AccessibleName = "Context help";
        _contextHelpButton.Dock = DockStyle.Right;
        _contextHelpButton.Name = "_contextHelpButton";
        _contextHelpButton.Size = new Size(29, 29);
        _contextHelpButton.TabStop = false;

        _contextHelpHost.Controls.Add(_contextHelpButton);
        _topRowHost.Controls.Add(_toolbar);
        _topRowHost.Controls.Add(_contextHelpHost);

        // Add controls
        Controls.Add(_grid);
        Controls.Add(_warningLabel);
        Controls.Add(_topRowHost);

        _topRowHost.ResumeLayout(false);
        ((ISupportInitialize)_grid).EndInit();
        _contextMenu.ResumeLayout(false);
        _contextHelpHost.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
