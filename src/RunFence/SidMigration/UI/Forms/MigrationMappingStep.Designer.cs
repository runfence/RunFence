#nullable disable

using System.ComponentModel;

namespace RunFence.SidMigration.UI.Forms;

partial class MigrationMappingStep
{
    private IContainer components = null;

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
        _loadingLabel = new Label();
        SuspendLayout();

        _loadingLabel.Location = new Point(15, 10);
        _loadingLabel.Size = new Size(560, 25);
        _loadingLabel.Text = "Resolving SIDs...";

        Controls.Add(_loadingLabel);

        AutoScaleMode = AutoScaleMode.Font;
        Size = new Size(595, 395);
        ResumeLayout(false);
    }
}
