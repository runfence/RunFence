#nullable disable

using System.ComponentModel;

namespace RunFence.Apps.UI.Forms;

partial class PathPrefixesSection
{
    private IContainer components = null;

    private GroupBox _contentGroup;

    public PathPrefixesSection()
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
        _contentGroup = new GroupBox();

        _contentGroup.SuspendLayout();
        SuspendLayout();

        // _contentGroup
        _contentGroup.Dock = DockStyle.Fill;
        _contentGroup.FlatStyle = FlatStyle.System;
        _contentGroup.Text = "Path Prefixes";
        _contentGroup.Controls.Add(_dataGrid);
        _contentGroup.Controls.Add(_toolStrip);

        // PathPrefixesSection
        Controls.Add(_contentGroup);

        _contentGroup.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
