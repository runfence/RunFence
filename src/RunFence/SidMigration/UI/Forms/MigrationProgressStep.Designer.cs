#nullable disable

using System.ComponentModel;

namespace RunFence.SidMigration.UI.Forms;

partial class MigrationProgressStep
{
    private IContainer components = null;

    public MigrationProgressStep()
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
        ProgressBar = new ProgressBar();
        StatusLabel = new Label();
        CancelButton = new Button();
        SuspendLayout();

        ProgressBar.Location = new Point(15, 30);
        ProgressBar.Size = new Size(560, 25);
        ProgressBar.Style = ProgressBarStyle.Marquee;

        StatusLabel.Location = new Point(15, 65);
        StatusLabel.Size = new Size(560, 25);
        StatusLabel.Text = "Scanning...";

        CancelButton.Text = "Cancel Scan";
        CancelButton.Location = new Point(15, 100);
        CancelButton.Size = new Size(100, 28);
        CancelButton.FlatStyle = FlatStyle.System;
        CancelButton.Visible = false;

        Controls.Add(ProgressBar);
        Controls.Add(StatusLabel);
        Controls.Add(CancelButton);

        AutoScaleMode = AutoScaleMode.Font;
        Size = new Size(595, 140);
        ResumeLayout(false);
    }
}
