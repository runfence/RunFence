using RunFence.Core.Models;

namespace RunFence.SidMigration.UI.Forms;

/// <summary>
/// UserControl for Step 5 (Disk Preview) of SidMigrationDialog.
/// Displays scan results in a grid with a count summary.
/// </summary>
public partial class SidMigrationPreviewStep : UserControl
{
    private const int MaxDisplayRows = 1000;
    private const int LargeChangeThreshold = 10000;

    public SidMigrationPreviewStep(IReadOnlyList<SidMigrationMatch> scanResults)
    {
        InitializeComponent();
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
}