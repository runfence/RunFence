#nullable disable

using System.ComponentModel;
using RunFence.UI.Controls;

namespace RunFence.Apps.UI.Forms;

partial class EnvVarsSection
{
    private IContainer components = null;

    private GroupBox _contentGroup;
    private ToolStrip _toolStrip;
    private ToolStripButton _addButton;
    private ToolStripButton _removeButton;
    private StyledDataGridView _dataGrid;
    private DataGridViewTextBoxColumn _nameColumn;
    private DataGridViewTextBoxColumn _valueColumn;
    private ContextMenuStrip _contextMenu;
    private ToolStripMenuItem _ctxAdd;
    private ToolStripMenuItem _ctxRemove;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _contentGroup = new GroupBox();
        _toolStrip = new ToolStrip();
        _addButton = new ToolStripButton();
        _removeButton = new ToolStripButton();
        _dataGrid = new StyledDataGridView();
        _nameColumn = new DataGridViewTextBoxColumn();
        _valueColumn = new DataGridViewTextBoxColumn();
        _contextMenu = new ContextMenuStrip();
        _ctxAdd = new ToolStripMenuItem();
        _ctxRemove = new ToolStripMenuItem();

        _contentGroup.SuspendLayout();
        _toolStrip.SuspendLayout();
        ((ISupportInitialize)_dataGrid).BeginInit();
        _contextMenu.SuspendLayout();
        SuspendLayout();

        // _contentGroup
        _contentGroup.Dock = DockStyle.Fill;
        _contentGroup.FlatStyle = FlatStyle.System;
        _contentGroup.Text = "Environment Variables";
        _contentGroup.Controls.Add(_dataGrid);
        _contentGroup.Controls.Add(_toolStrip);

        // _toolStrip
        _toolStrip.Dock = DockStyle.Top;
        _toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _toolStrip.RenderMode = ToolStripRenderMode.System;
        _toolStrip.ImageScalingSize = new Size(24, 24);
        _toolStrip.Items.AddRange(new ToolStripItem[] { _addButton, _removeButton });

        // _addButton
        _addButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _addButton.ToolTipText = "Add row";
        _addButton.Click += OnAddClick;

        // _removeButton
        _removeButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _removeButton.ToolTipText = "Remove";
        _removeButton.Enabled = false;
        _removeButton.Click += OnRemoveClick;

        // _dataGrid
        _dataGrid.Dock = DockStyle.Fill;
        _dataGrid.AllowUserToAddRows = true;
        _dataGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _dataGrid.MultiSelect = false;
        _dataGrid.AutoGenerateColumns = false;
        _dataGrid.Columns.AddRange(new DataGridViewColumn[] { _nameColumn, _valueColumn });
        _dataGrid.ContextMenuStrip = _contextMenu;
        _dataGrid.CurrentCellChanged += OnCurrentCellChanged;
        _dataGrid.MouseDown += OnGridMouseDown;
        _dataGrid.KeyDown += OnGridKeyDown;

        // _nameColumn
        _nameColumn.HeaderText = "Name";
        _nameColumn.Width = 150;
        _nameColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;

        // _valueColumn
        _valueColumn.HeaderText = "Value";
        _valueColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        // _contextMenu
        _contextMenu.Items.AddRange(new ToolStripItem[] { _ctxAdd, _ctxRemove });
        _contextMenu.Opening += OnContextMenuOpening;

        // _ctxAdd
        _ctxAdd.Text = "Add row";
        _ctxAdd.Click += OnAddClick;

        // _ctxRemove
        _ctxRemove.Text = "Remove";
        _ctxRemove.Click += OnRemoveClick;

        // EnvVarsSection
        AutoScaleMode = AutoScaleMode.Inherit;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        Controls.Add(_contentGroup);

        _contentGroup.ResumeLayout(false);
        _toolStrip.ResumeLayout(false);
        _toolStrip.PerformLayout();
        ((ISupportInitialize)_dataGrid).EndInit();
        _contextMenu.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
