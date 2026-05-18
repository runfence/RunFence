using RunFence.SidMigration.UI;

namespace RunFence.SidMigration.UI.Forms;

public sealed class SidMigrationDiskApplyStepView : UserControl, ISidMigrationDiskApplyStepView
{
    private readonly SidMigrationDiskApplyProgressStep _progressStep;
    private readonly Label _pathLabel;

    public SidMigrationDiskApplyStepView()
    {
        var pathFontFamily = SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;

        _progressStep = new SidMigrationDiskApplyProgressStep
        {
            Dock = DockStyle.Top
        };
        _pathLabel = new Label
        {
            Dock = DockStyle.Top,
            Padding = new Padding(12, 4, 12, 0),
            AutoSize = false,
            Height = 22,
            ForeColor = Color.DarkGray,
            Font = new Font(pathFontFamily, 8f)
        };

        Controls.Add(_progressStep);
        Controls.Add(_pathLabel);
    }

    Control ISidMigrationStepView.View => this;

    public ProgressBar ProgressBar => _progressStep.ProgressBar;

    public Label StatusLabel => _progressStep.StatusLabel;

    public Button CancelButton => _progressStep.CancelButton;

    public void Configure(string statusText, int? maxValue, bool showCancelButton)
        => _progressStep.Configure(statusText, maxValue, showCancelButton);

    public void SetCurrentPath(string currentPath)
    {
        _pathLabel.Text = currentPath;
    }
}
