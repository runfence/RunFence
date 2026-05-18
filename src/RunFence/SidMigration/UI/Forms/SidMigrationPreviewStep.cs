using RunFence.Core.Models;

namespace RunFence.SidMigration.UI.Forms;

/// <summary>
/// UserControl for Step 5 (Disk Preview) of SidMigrationDialog.
/// Displays scan results in a grid with a count summary.
/// </summary>
public partial class SidMigrationPreviewStep : UserControl, ISidMigrationStepView
{
    private const int MaxDisplayRows = 1000;
    private const int LargeChangeThreshold = 10000;

    public SidMigrationPreviewStep(IReadOnlyList<SidMigrationMatch> scanResults)
    {
        InitializeComponent();
        AdjustLayout();
        PopulateGrid(scanResults);
    }
    private void PopulateGrid(IReadOnlyList<SidMigrationMatch> scanResults)
    {
        foreach (var hit in scanResults.Take(MaxDisplayRows))
        {
            var changes = new List<string>();
            if (hit.MatchType.HasFlag(SidMigrationMatchType.Ace))
                changes.Add($"ACEs: {hit.AceCountByOldSid.Values.Sum()}");
            if (hit.MatchType.HasFlag(SidMigrationMatchType.Owner))
                changes.Add("Owner");
            _grid.Rows.Add(hit.Path, hit.IsDirectory ? "Dir" : "File", string.Join(", ", changes));
        }

        _summaryLabel.Text = $"Total: {scanResults.Count:N0} items" +
                             (scanResults.Count > MaxDisplayRows ? $" (showing first {MaxDisplayRows:N0})" : "");

        _warningLabel.Visible = scanResults.Count > LargeChangeThreshold;
    }

    private void AdjustLayout()
    {
        var descriptionHeight = TextRenderer.MeasureText(
            _descriptionLabel.Text,
            _descriptionLabel.Font,
            new Size(_descriptionLabel.Width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
        _descriptionLabel.Height = descriptionHeight;
        _grid.Top = _descriptionLabel.Bottom + 5;
        _grid.Height = Math.Max(120, Height - _grid.Top - 75);
        _summaryLabel.Top = _grid.Bottom + 10;
        _warningLabel.Top = _summaryLabel.Bottom + 5;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        AdjustLayout();
    }

    Control ISidMigrationStepView.View => this;
}
