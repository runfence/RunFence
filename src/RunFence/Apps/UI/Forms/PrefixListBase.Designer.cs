#nullable disable

using System.ComponentModel;
using RunFence.UI.Controls;

namespace RunFence.Apps.UI.Forms;

partial class PrefixListBase
{
    private IContainer components = null;

    protected ToolStrip _toolStrip;
    protected ToolStripButton _addButton;
    protected ToolStripButton _addManualButton;
    protected ToolStripButton _removeButton;
    protected StyledDataGridView _dataGrid;
    protected DataGridViewTextBoxColumn colPrefix;
    protected ContextMenuStrip _contextMenu;
    protected ToolStripMenuItem _ctxAdd;
    protected ToolStripMenuItem _ctxAddManual;
    protected ToolStripMenuItem _ctxRemove;

    public PrefixListBase()
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
        _addManualButton = new ToolStripButton();
        _removeButton = new ToolStripButton();
        _dataGrid = new StyledDataGridView();
        colPrefix = new DataGridViewTextBoxColumn();
        _contextMenu = new ContextMenuStrip();
        _ctxAdd = new ToolStripMenuItem();
        _ctxAddManual = new ToolStripMenuItem();
        _ctxRemove = new ToolStripMenuItem();

        _toolStrip.SuspendLayout();
        ((ISupportInitialize)_dataGrid).BeginInit();
        _contextMenu.SuspendLayout();

        // _toolStrip
        _toolStrip.Dock = DockStyle.Top;
        _toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _toolStrip.RenderMode = ToolStripRenderMode.System;
        _toolStrip.ImageScalingSize = new Size(24, 24);
        _toolStrip.Items.AddRange(new ToolStripItem[] { _addButton, _addManualButton, _removeButton });

        // _addButton
        _addButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _addButton.ToolTipText = "Add Folder";
        _addButton.Click += OnAddClick;

        // _addManualButton
        _addManualButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _addManualButton.ToolTipText = "Add URL";
        _addManualButton.Click += OnAddManualClick;

        // _removeButton
        _removeButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _removeButton.ToolTipText = "Remove prefix";
        _removeButton.Enabled = false;
        _removeButton.Click += OnRemoveClick;

        // _dataGrid
        _dataGrid.Dock = DockStyle.Fill;
        _dataGrid.ColumnHeadersVisible = false;
        _dataGrid.ContextMenuStrip = _contextMenu;
        _dataGrid.Columns.AddRange(new DataGridViewColumn[] { colPrefix });
        _dataGrid.SelectionChanged += OnSelectionChanged;
        _dataGrid.KeyDown += OnKeyDown;
        _dataGrid.MouseDown += OnMouseDown;

        // colPrefix
        colPrefix.HeaderText = "Prefix";
        colPrefix.Name = "colPrefix";
        colPrefix.ReadOnly = false;
        colPrefix.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        // _contextMenu
        _contextMenu.Items.AddRange(new ToolStripItem[] { _ctxAdd, _ctxAddManual, _ctxRemove });
        _contextMenu.Opening += OnContextMenuOpening;

        // _ctxAdd
        _ctxAdd.Text = "Add Folder";
        _ctxAdd.Click += OnAddClick;

        // _ctxAddManual
        _ctxAddManual.Text = "Add URL";
        _ctxAddManual.Click += OnAddManualClick;

        // _ctxRemove
        _ctxRemove.Text = "Remove";
        _ctxRemove.Click += OnRemoveClick;

        // NOTE: _dataGrid and _toolStrip are NOT added to Controls here.
        // Derived classes add them into their own container (GroupBox etc.).

        AutoScaleMode = AutoScaleMode.Inherit;
        Margin = Padding.Empty;

        _toolStrip.ResumeLayout(false);
        _toolStrip.PerformLayout();
        ((ISupportInitialize)_dataGrid).EndInit();
        _contextMenu.ResumeLayout(false);
    }
}
