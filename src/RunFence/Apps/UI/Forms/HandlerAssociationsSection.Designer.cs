#nullable disable

using System.ComponentModel;
using RunFence.UI.Controls;

namespace RunFence.Apps.UI.Forms;

partial class HandlerAssociationsSection
{
    private IContainer components = null;

    private ToolStrip _toolStrip;
    private ToolStripButton _addButton;
    private ToolStripButton _removeButton;
    private StyledDataGridView _dataGrid;
    private DataGridViewTextBoxColumn _keyColumn;
    private DataGridViewTextBoxColumn _argsTemplateColumn;
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
        _toolStrip = new ToolStrip();
        _addButton = new ToolStripButton();
        _removeButton = new ToolStripButton();
        _dataGrid = new StyledDataGridView();
        _keyColumn = new DataGridViewTextBoxColumn();
        _argsTemplateColumn = new DataGridViewTextBoxColumn();
        _contextMenu = new ContextMenuStrip();
        _ctxAdd = new ToolStripMenuItem();
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
        _toolStrip.Items.AddRange(new ToolStripItem[] { _addButton, _removeButton });

        // _addButton
        _addButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _addButton.ToolTipText = "Add association...";
        _addButton.Click += OnAddClick;

        // _removeButton
        _removeButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _removeButton.ToolTipText = "Remove association";
        _removeButton.Enabled = false;
        _removeButton.Click += OnRemoveClick;

        // _dataGrid
        _dataGrid.Dock = DockStyle.Fill;
        _dataGrid.AllowUserToAddRows = false;
        _dataGrid.AllowUserToDeleteRows = false;
        _dataGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _dataGrid.MultiSelect = false;
        _dataGrid.ContextMenuStrip = _contextMenu;
        _dataGrid.Columns.AddRange(new DataGridViewColumn[] { _keyColumn, _argsTemplateColumn });
        _dataGrid.SelectionChanged += OnSelectionChanged;
        _dataGrid.KeyDown += OnKeyDown;
        _dataGrid.MouseDown += OnMouseDown;

        // _keyColumn
        _keyColumn.HeaderText = "Association";
        _keyColumn.Name = "colKey";
        _keyColumn.ReadOnly = true;
        _keyColumn.Width = 130;
        _keyColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;

        // _argsTemplateColumn
        _argsTemplateColumn.HeaderText = "Args Template";
        _argsTemplateColumn.Name = "colArgsTemplate";
        _argsTemplateColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        // _contextMenu
        _contextMenu.Items.AddRange(new ToolStripItem[] { _ctxAdd, _ctxRemove });
        _contextMenu.Opening += OnContextMenuOpening;

        // _ctxAdd
        _ctxAdd.Text = "Add...";
        _ctxAdd.Click += OnAddClick;

        // _ctxRemove
        _ctxRemove.Text = "Remove";
        _ctxRemove.Click += OnRemoveClick;

        // HandlerAssociationsSection
        AutoScaleMode = AutoScaleMode.Inherit;
        Dock = DockStyle.Fill;
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
