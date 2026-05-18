namespace RunFence.Account.UI.OrphanedProfiles;

public partial class OrphanedProfilesReportPanel : UserControl
{
    private List<string> _deleted = new();
    private List<(string Path, string Error)> _failed = new();

    public void Populate(List<string> deleted, List<(string Path, string Error)> failed)
    {
        _deleted = deleted;
        _failed = failed;

        _summaryLabel.Text = $"Move to Recycle Bin complete. {deleted.Count} moved, {failed.Count} failed.";

        _resultListView.Items.Clear();
        _resultListView.Groups.Clear();

        var deletedGroup = new ListViewGroup("Moved to Recycle Bin", HorizontalAlignment.Left) { Name = "Deleted" };
        var failedGroup = new ListViewGroup("Failed", HorizontalAlignment.Left) { Name = "Failed" };
        _resultListView.Groups.Add(deletedGroup);
        _resultListView.Groups.Add(failedGroup);

        foreach (var path in deleted)
        {
            var item = new ListViewItem(path) { Group = deletedGroup, ForeColor = Color.FromArgb(0x00, 0x88, 0x00) };
            item.SubItems.Add("Moved to Recycle Bin");
            item.SubItems.Add("");
            _resultListView.Items.Add(item);
        }

        foreach (var (path, error) in failed)
        {
            var item = new ListViewItem(path) { Group = failedGroup, ForeColor = Color.FromArgb(0xCC, 0x33, 0x33) };
            item.SubItems.Add("Failed");
            item.SubItems.Add(error);
            _resultListView.Items.Add(item);
        }
    }

    private void OnCopyClick(object? sender, EventArgs e)
    {
        var lines = new List<string>
        {
            "Profile Move to Recycle Bin Report",
            new('-', 60)
        };
        foreach (var path in _deleted)
            lines.Add($"[Moved to Recycle Bin] {path}");
        foreach (var (path, error) in _failed)
            lines.Add($"[Failed]  {path} \u2014 {error}");
        Clipboard.SetText(string.Join(Environment.NewLine, lines));
        MessageBox.Show("Copied to clipboard.", ParentForm?.Text ?? "Report", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
