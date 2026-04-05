#nullable disable

using System.ComponentModel;

namespace RunFence.UI.Forms;

partial class IpcCallerSection
{
    private IContainer components = null;

    private Label _titleLabel;
    private Label _descLabel;
    private ToolStrip _toolStrip;
    private ToolStripButton _addButton;
    private ToolStripButton _removeButton;
    private ListBox _listBox;
    private ContextMenuStrip _contextMenu;
    private ToolStripMenuItem _ctxAdd;
    private ToolStripMenuItem _ctxRemove;

    private IpcCallerSection() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _titleLabel = new Label();
        _descLabel = new Label();
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

        // _titleLabel (shown only when SetGroupTitle is called)
        _titleLabel.Dock = DockStyle.Top;
        _titleLabel.Height = 20;
        _titleLabel.AutoSize = false;
        _titleLabel.Visible = false;
        _titleLabel.Font = new Font(DefaultFont, FontStyle.Bold);

        // _descLabel
        _descLabel.Dock = DockStyle.Top;
        _descLabel.Height = 20;
        _descLabel.AutoSize = false;
        _descLabel.Visible = false;

        // _toolStrip
        _toolStrip.Dock = DockStyle.Top;
        _toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _toolStrip.RenderMode = ToolStripRenderMode.System;
        _toolStrip.ImageScalingSize = new Size(24, 24);
        _toolStrip.Items.AddRange(new ToolStripItem[] { _addButton, _removeButton });

        // _addButton
        _addButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _addButton.ToolTipText = "Add...";
        _addButton.Click += OnAddClick;

        // _removeButton
        _removeButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _removeButton.ToolTipText = "Remove";
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

        // IpcCallerSection — add Fill first, then Top items (last added = topmost in DockStyle.Top stack)
        AutoScaleMode = AutoScaleMode.Inherit;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        Controls.Add(_listBox);
        Controls.Add(_toolStrip);
        Controls.Add(_descLabel);
        Controls.Add(_titleLabel);

        _toolStrip.ResumeLayout(false);
        _toolStrip.PerformLayout();
        _contextMenu.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
