#nullable disable

using System.ComponentModel;

namespace RunFence.SidMigration.UI.Forms;

partial class SidMigrationDiskApplyProgressStep
{
    private IContainer components = null;

    internal SidMigrationDiskApplyProgressStep()
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
        DescriptionLabel.Text = "The approved filesystem changes are now being written to disk. Permissions and ownership references are being replaced or removed according to the review step, so this is no longer a dry run. Let this finish before closing the wizard, or disk state and saved settings will be left out of sync.";
        ResumeLayout(false);
    }
}
