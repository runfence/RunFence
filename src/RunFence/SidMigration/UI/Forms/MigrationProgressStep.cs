namespace RunFence.SidMigration.UI.Forms;

/// <summary>
/// Shared progress step layout used by migration steps 2, 4, and 6.
/// Contains a progress bar, status label, and optional cancel button.
/// </summary>
public partial class MigrationProgressStep : UserControl
{
    public ProgressBar ProgressBar { get; private set; }

    public Label StatusLabel { get; private set; }

    public Button CancelButton { get; private set; }

    /// <summary>
    /// Configures the step for indeterminate scanning (marquee) or bounded progress.
    /// </summary>
    public void Configure(string statusText, int? maxValue, bool showCancelButton)
    {
        StatusLabel.Text = statusText;

        if (maxValue.HasValue)
        {
            ProgressBar.Style = ProgressBarStyle.Continuous;
            ProgressBar.Minimum = 0;
            ProgressBar.Maximum = maxValue.Value;
        }
        else
        {
            ProgressBar.Style = ProgressBarStyle.Marquee;
        }

        CancelButton.Visible = showCancelButton;
    }

    /// <summary>Resets the progress bar to the empty state.</summary>
    public void ResetProgress()
    {
        ProgressBar.Style = ProgressBarStyle.Continuous;
        ProgressBar.Value = 0;
    }
}