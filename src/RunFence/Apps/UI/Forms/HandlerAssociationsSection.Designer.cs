#nullable disable

using System.ComponentModel;

namespace RunFence.Apps.UI.Forms;

partial class HandlerAssociationsSection
{
    private IContainer components = null;

    private ToolStrip _toolStrip;
    private ToolStripButton _addButton;
    private ToolStripButton _removeButton;
    private ListBox _listBox;
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
        _listBox = new ListBox();
        _contextMenu = new ContextMenuStrip();
        _ctxAdd = new ToolStripMenuItem();
        _ctxRemove = new ToolStripMenuItem();

        _toolStrip.SuspendLayout();
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

        // _listBox
        _listBox.Dock = DockStyle.Fill;
        _listBox.IntegralHeight = false;
        _listBox.ContextMenuStrip = _contextMenu;
        _listBox.SelectedIndexChanged += OnSelectionChanged;
        _listBox.KeyDown += OnKeyDown;
        _listBox.MouseDown += OnMouseDown;

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
        Controls.Add(_listBox);
        Controls.Add(_toolStrip);

        _toolStrip.ResumeLayout(false);
        _toolStrip.PerformLayout();
        _contextMenu.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
