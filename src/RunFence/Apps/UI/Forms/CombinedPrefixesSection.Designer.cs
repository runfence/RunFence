#nullable disable

using System.ComponentModel;

namespace RunFence.Apps.UI.Forms;

partial class CombinedPrefixesSection
{
    private IContainer components = null;

    private GroupBox _contentGroup;
    private ToolStripSeparator _modeSeparator;
    private ToolStripControlHost _radioHost;
    private FlowLayoutPanel _radioPanel;
    private RadioButton _addRadio;
    private RadioButton _replaceRadio;
    private Font _headerFont;

    public CombinedPrefixesSection()
    {
        InitializeComponent();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _headerFont?.Dispose();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _contentGroup = new GroupBox();
        _modeSeparator = new ToolStripSeparator();
        _radioPanel = new FlowLayoutPanel();
        _addRadio = new RadioButton();
        _replaceRadio = new RadioButton();
        _radioHost = new ToolStripControlHost(_radioPanel);

        _contentGroup.SuspendLayout();
        _radioPanel.SuspendLayout();
        SuspendLayout();

        // _contentGroup — fills the editor area below the hosted mode selector
        _contentGroup.Dock = DockStyle.Fill;
        _contentGroup.FlatStyle = FlatStyle.System;
        _contentGroup.Text = "Path Prefixes";
        _contentGroup.Controls.Add(_dataGrid);
        _contentGroup.Controls.Add(_toolStrip);
        _toolStrip.Items.AddRange(new ToolStripItem[] { _modeSeparator, _radioHost });

        // _modeSeparator
        _modeSeparator.Alignment = ToolStripItemAlignment.Right;

        // _radioHost
        _radioHost.Alignment = ToolStripItemAlignment.Right;
        _radioHost.AutoSize = true;
        _radioHost.Margin = new Padding(0);
        _radioHost.Padding = Padding.Empty;

        // _radioPanel — hosted inside the ToolStrip so the mode selector stays visible
        // even when the grid fills the rest of the control.
        _radioPanel.AutoSize = true;
        _radioPanel.FlowDirection = FlowDirection.LeftToRight;
        _radioPanel.WrapContents = false;
        _radioPanel.Margin = Padding.Empty;
        _radioPanel.Padding = new Padding(0);

        // _addRadio
        _addRadio.Text = "Add (union)";
        _addRadio.AutoSize = true;
        _addRadio.Checked = true;
        _addRadio.Margin = new Padding(0, 3, 12, 0);

        // _replaceRadio
        _replaceRadio.Text = "Replace";
        _replaceRadio.AutoSize = true;
        _replaceRadio.Margin = new Padding(0, 3, 0, 0);

        _radioPanel.Controls.Add(_addRadio);
        _radioPanel.Controls.Add(_replaceRadio);

        // CombinedPrefixesSection
        AutoScaleMode = AutoScaleMode.Inherit;
        Margin = Padding.Empty;
        Controls.Add(_contentGroup);

        _contentGroup.ResumeLayout(false);
        _radioPanel.ResumeLayout(false);
        _radioPanel.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }
}
