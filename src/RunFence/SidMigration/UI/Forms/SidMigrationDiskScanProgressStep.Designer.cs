#nullable disable

using System.ComponentModel;

namespace RunFence.SidMigration.UI.Forms;

partial class SidMigrationDiskScanProgressStep
{
    private IContainer components = null;

    internal SidMigrationDiskScanProgressStep()
    {
        InitializeComponent();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        SuspendLayout();
        DescriptionLabel.Text = "The selected paths are now being scanned for real filesystem matches to the replacements and removals you approved. This is still a read-only phase, but it is no longer discovering candidate identities; it is finding the exact files and folders that would be changed by the next step. Use this phase to wait for the apply preview, not to choose new mappings.";
        ResumeLayout(false);
    }
}
