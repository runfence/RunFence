namespace RunFence.SidMigration.UI.Forms;

/// <summary>
/// Shared progress step layout used by migration steps 2, 4, and 6.
/// Concrete derived controls own the visible description text in their designer files.
/// </summary>
public partial class MigrationProgressStep : UserControl, ISidMigrationProgressStepView
{
    protected override Size DefaultSize => new Size(595, 140);

    public ProgressBar ProgressBar { get; private set; }

    public Label StatusLabel { get; private set; }

    public Button CancelButton { get; private set; }

    Control ISidMigrationStepView.View => this;

    /// <summary>
    /// Configures the step for indeterminate scanning (marquee) or bounded progress.
    /// </summary>
    public void Configure(string statusText, int? maxValue, bool showCancelButton)
    {
        ResizeDescriptionLabel();
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

    private void ResizeDescriptionLabel()
    {
        var availableWidth = Math.Max(1, Width - DescriptionLabel.Padding.Horizontal);
        var measured = TextRenderer.MeasureText(
            DescriptionLabel.Text,
            DescriptionLabel.Font,
            new Size(availableWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        DescriptionLabel.Height = measured.Height + DescriptionLabel.Padding.Vertical;
        ProgressBar.Top = DescriptionLabel.Bottom;
        StatusLabel.Top = ProgressBar.Bottom + 10;
        CancelButton.Top = StatusLabel.Bottom + 5;
        Height = CancelButton.Bottom + 10;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ResizeDescriptionLabel();
    }
}
