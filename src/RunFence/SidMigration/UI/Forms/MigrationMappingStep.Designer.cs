#nullable disable

using System.ComponentModel;

namespace RunFence.SidMigration.UI.Forms;

partial class MigrationMappingStep
{
    private IContainer components = null;

    private Label _descriptionLabel;
    private Label _loadingLabel;

    private MigrationMappingStep() { InitializeComponent(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _descriptionLabel = new Label();
        _loadingLabel = new Label();
        SuspendLayout();

        _descriptionLabel.Dock = DockStyle.Top;
        _descriptionLabel.AutoSize = false;
        _descriptionLabel.Padding = new Padding(0, 0, 0, 8);
        _descriptionLabel.Size = new Size(595, 72);
        _descriptionLabel.Text = "Review each old security identity and decide whether it should be replaced with a current account or removed entirely. Replace keeps access by moving permissions and saved references to a new owner, while Remove deletes stale references that should no longer grant anything. Use Replace when the data is still needed by a living account, and Remove when the old identity should disappear from both disk permissions and saved settings.";

        _loadingLabel.Location = new Point(15, 80);
        _loadingLabel.Size = new Size(560, 25);
        _loadingLabel.Text = "Resolving SIDs...";

        Controls.Add(_descriptionLabel);
        Controls.Add(_loadingLabel);

        AutoScaleMode = AutoScaleMode.Font;
        Size = new Size(595, 395);
        ResumeLayout(false);
    }
}
