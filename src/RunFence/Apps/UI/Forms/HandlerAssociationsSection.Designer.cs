#nullable disable

using System.ComponentModel;
using RunFence.UI.Controls;

namespace RunFence.Apps.UI.Forms;

partial class HandlerAssociationsSection
{
    private IContainer components = null;

    private ToolStrip _toolStrip;
    private ToolStripButton _addButton;
    private ToolStripButton _editButton;
    private ToolStripButton _removeButton;
    private StyledDataGridView _dataGrid;
    private DataGridViewTextBoxColumn colKey;
    private DataGridViewTextBoxColumn colArgsTemplate;
    private ContextMenuStrip _contextMenu;
    private ToolStripMenuItem _ctxAdd;
    private ToolStripMenuItem _ctxEdit;
    private ToolStripMenuItem _ctxRemove;

    public HandlerAssociationsSection()
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
        _toolStrip = new ToolStrip();
        _addButton = new ToolStripButton();
        _editButton = new ToolStripButton();
        _removeButton = new ToolStripButton();
        _dataGrid = new StyledDataGridView();
        colKey = new DataGridViewTextBoxColumn();
        colArgsTemplate = new DataGridViewTextBoxColumn();
        _contextMenu = new ContextMenuStrip();
        _ctxAdd = new ToolStripMenuItem();
        _ctxEdit = new ToolStripMenuItem();
        _ctxRemove = new ToolStripMenuItem();

        _toolStrip.SuspendLayout();
        ((ISupportInitialize)_dataGrid).BeginInit();
        _contextMenu.SuspendLayout();
        SuspendLayout();

        // _toolStrip
        _toolStrip.Dock = DockStyle.Top;
        _toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _toolStrip.RenderMode = ToolStripRenderMode.System;
        _toolStrip.ImageScalingSize = new Size(24, 24);
        _toolStrip.Items.AddRange(new ToolStripItem[] { _addButton, _editButton, _removeButton });

        // _addButton
        _addButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _addButton.ToolTipText = "Add association...";
        _addButton.Enabled = false;
        _addButton.Click += OnAddClick;

        // _editButton
        _editButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _editButton.ToolTipText = "Edit association\u2026";
        _editButton.Enabled = false;
        _editButton.Click += OnEditClick;

        // _removeButton
        _removeButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _removeButton.ToolTipText = "Remove association";
        _removeButton.Enabled = false;
        _removeButton.Click += OnRemoveClick;

        // _dataGrid
        _dataGrid.Dock = DockStyle.Fill;
        _dataGrid.ContextMenuStrip = _contextMenu;
        _dataGrid.Enabled = false;
        _dataGrid.Columns.AddRange(new DataGridViewColumn[] { colKey, colArgsTemplate });
        _dataGrid.CellDoubleClick += OnCellDoubleClick;
        _dataGrid.SelectionChanged += OnSelectionChanged;
        _dataGrid.KeyDown += OnKeyDown;
        _dataGrid.MouseDown += OnMouseDown;
        _dataGrid.MouseUp += OnMouseUp;

        // colKey
        colKey.HeaderText = "Association";
        colKey.Name = "colKey";
        colKey.ReadOnly = true;
        colKey.Width = 130;
        colKey.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;

        // colArgsTemplate
        colArgsTemplate.HeaderText = "Args Template";
        colArgsTemplate.Name = "colArgsTemplate";
        colArgsTemplate.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        colArgsTemplate.ReadOnly = true;

        // _contextMenu
        _contextMenu.Items.AddRange(new ToolStripItem[] { _ctxAdd, _ctxEdit, _ctxRemove });
        _contextMenu.Opening += OnContextMenuOpening;

        // _ctxAdd
        _ctxAdd.Text = "Add...";
        _ctxAdd.Click += OnAddClick;

        // _ctxEdit
        _ctxEdit.Text = "Edit\u2026";
        _ctxEdit.Click += OnEditClick;

        // _ctxRemove
        _ctxRemove.Text = "Remove";
        _ctxRemove.Click += OnRemoveClick;

        // HandlerAssociationsSection
        AutoScaleMode = AutoScaleMode.Inherit;
        Margin = Padding.Empty;
        Controls.Add(_dataGrid);
        Controls.Add(_toolStrip);

        _toolStrip.ResumeLayout(false);
        _toolStrip.PerformLayout();
        ((ISupportInitialize)_dataGrid).EndInit();
        _contextMenu.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
