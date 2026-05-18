#nullable disable

using System.ComponentModel;

namespace RunFence.SidMigration.UI.Forms;

partial class SidMigrationDiscoveryProgressStep
{
    private IContainer components = null;

    internal SidMigrationDiscoveryProgressStep()
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
        DescriptionLabel.Text = "The scan is collecting the old security identities that appear on the selected paths. This does not change disk state yet; it only builds the list you will review next. Use this phase to wait for scope discovery, not to judge final replacement decisions.";
        ResumeLayout(false);
    }
}
