#nullable disable

using System.ComponentModel;

namespace RunFence.Persistence.UI.Forms;

internal partial class ConfigManagerSection
{
    private IContainer components = null;

    private GroupBox _configsGroup;
    private Label _configDesc;
    private ToolStrip _configToolStrip;
    private ToolStripButton _configNewButton;
    private ToolStripButton _configLoadButton;
    private ToolStripButton _configUnloadButton;
    private ToolStripButton _configExportButton;
    private ToolStripButton _configImportButton;
    private ListBox _configListBox;
    private ContextMenuStrip _configContextMenu;
    private ToolStripMenuItem _ctxConfigUnload;
    private ToolStripSeparator _ctxConfigSepExport;
    private ToolStripMenuItem _ctxConfigExportCtx;
    private ToolStripMenuItem _ctxConfigImportCtx;

    private ConfigManagerSection() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _configsGroup = new GroupBox();
        _configDesc = new Label();
        _configToolStrip = new ToolStrip();
        _configNewButton = new ToolStripButton();
        _configLoadButton = new ToolStripButton();
        _configUnloadButton = new ToolStripButton();
        _configExportButton = new ToolStripButton();
        _configImportButton = new ToolStripButton();
        _configListBox = new ListBox();
        _configContextMenu = new ContextMenuStrip();
        _ctxConfigUnload = new ToolStripMenuItem();
        _ctxConfigSepExport = new ToolStripSeparator();
        _ctxConfigExportCtx = new ToolStripMenuItem();
        _ctxConfigImportCtx = new ToolStripMenuItem();

        _configsGroup.SuspendLayout();
        _configToolStrip.SuspendLayout();
        _configContextMenu.SuspendLayout();
        SuspendLayout();

        // _configsGroup
        _configsGroup.Text = "App Configs";
        _configsGroup.Dock = DockStyle.Fill;
        _configsGroup.FlatStyle = FlatStyle.System;
        // Fill first, then Top items (last added = topmost in DockStyle.Top stack)
        _configsGroup.Controls.Add(_configListBox);
        _configsGroup.Controls.Add(_configToolStrip);
        _configsGroup.Controls.Add(_configDesc);

        // _configDesc
        _configDesc.Text = "Main config and loaded additional configs. Loaded configs are not persisted across restarts. Exported config contains app entries without credentials.";
        _configDesc.Dock = DockStyle.Top;
        _configDesc.AutoSize = false;
        _configDesc.Resize += OnConfigDescResize;

        // _configToolStrip
        _configToolStrip.Dock = DockStyle.Top;
        _configToolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _configToolStrip.RenderMode = ToolStripRenderMode.System;
        _configToolStrip.ImageScalingSize = new Size(20, 20);
        _configToolStrip.Items.AddRange(new ToolStripItem[]
        {
            _configNewButton, _configLoadButton, _configUnloadButton,
            new ToolStripSeparator(),
            _configExportButton, _configImportButton
        });

        // _configNewButton
        _configNewButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _configNewButton.ToolTipText = "New config...";
        _configNewButton.Click += OnNewConfigClick;

        // _configLoadButton
        _configLoadButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _configLoadButton.ToolTipText = "Load config...";
        _configLoadButton.Click += OnLoadConfigClick;

        // _configUnloadButton
        _configUnloadButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _configUnloadButton.ToolTipText = "Unload selected config";
        _configUnloadButton.Enabled = false;
        _configUnloadButton.Click += OnUnloadConfigClick;

        // _configExportButton
        _configExportButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _configExportButton.ToolTipText = "Export selected config as JSON...";
        _configExportButton.Enabled = false;
        _configExportButton.Click += OnExportConfigClick;

        // _configImportButton
        _configImportButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        _configImportButton.ToolTipText = "Import JSON into selected config...";
        _configImportButton.Enabled = false;
        _configImportButton.Click += OnImportConfigClick;

        // _configListBox
        _configListBox.Dock = DockStyle.Fill;
        _configListBox.IntegralHeight = false;
        _configListBox.ContextMenuStrip = _configContextMenu;
        _configListBox.SelectedIndexChanged += OnConfigSelectionChanged;
        _configListBox.MouseDown += OnConfigMouseDown;

        // _configContextMenu
        _configContextMenu.Items.AddRange(new ToolStripItem[]
        {
            _ctxConfigUnload,
            _ctxConfigSepExport, _ctxConfigExportCtx, _ctxConfigImportCtx
        });
        _configContextMenu.Opening += OnConfigContextMenuOpening;

        // _ctxConfigUnload
        _ctxConfigUnload.Text = "Unload";
        _ctxConfigUnload.Click += OnUnloadConfigClick;

        // _ctxConfigExportCtx
        _ctxConfigExportCtx.Text = "Export as JSON...";
        _ctxConfigExportCtx.Click += OnExportConfigClick;

        // _ctxConfigImportCtx
        _ctxConfigImportCtx.Text = "Import JSON...";
        _ctxConfigImportCtx.Click += OnImportConfigClick;

        // ConfigManagerSection
        AutoScaleMode = AutoScaleMode.Inherit;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        Controls.Add(_configsGroup);

        _configsGroup.ResumeLayout(false);
        _configToolStrip.ResumeLayout(false);
        _configToolStrip.PerformLayout();
        _configContextMenu.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
