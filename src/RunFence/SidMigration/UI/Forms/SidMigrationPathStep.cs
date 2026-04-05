namespace RunFence.SidMigration.UI.Forms;

/// <summary>
/// UserControl for Step 1 (Path Selection) of SidMigrationDialog.
/// Owns the checked list box of drive/folder paths and exposes the selection state.
/// </summary>
public partial class SidMigrationPathStep : UserControl
{
    /// <summary>Raised when the user clicks the "Skip — I know the SIDs" button.</summary>
    public event EventHandler? SkipRequested;

    public SidMigrationPathStep(bool showSkipButton)
    {
        InitializeComponent();

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive is { DriveType: DriveType.Fixed, IsReady: true })
                _pathListBox.Items.Add(drive.RootDirectory.FullName, false);
        }

        if (!showSkipButton)
            _skipButton.Visible = false;
    }

    /// <summary>
    /// Restores a previously saved path state (list items and their checked status).
    /// </summary>
    public void RestoreState(List<(string path, bool isChecked)> savedState)
    {
        _pathListBox.Items.Clear();
        foreach (var (path, isChecked) in savedState)
            _pathListBox.Items.Add(path, isChecked);
    }

    /// <summary>
    /// Reads the selected (checked) paths and saves state for later restoration.
    /// Returns the list of checked paths.
    /// </summary>
    public List<string> CollectSelectedPaths()
    {
        SavedState = new List<(string, bool)>();
        var selected = new List<string>();

        for (int i = 0; i < _pathListBox.Items.Count; i++)
        {
            var path = _pathListBox.Items[i].ToString()!;
            var isChecked = _pathListBox.GetItemChecked(i);
            SavedState.Add((path, isChecked));
            if (isChecked)
                selected.Add(path);
        }

        return selected;
    }

    public List<(string path, bool isChecked)>? SavedState { get; private set; }

    private void OnSelectAllClick(object? sender, EventArgs e)
    {
        for (int i = 0; i < _pathListBox.Items.Count; i++)
            _pathListBox.SetItemChecked(i, true);
    }

    private void OnDeselectAllClick(object? sender, EventArgs e)
    {
        for (int i = 0; i < _pathListBox.Items.Count; i++)
            _pathListBox.SetItemChecked(i, false);
    }

    private void OnAddPathClick(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog();
        dlg.Description = "Select additional path";
        dlg.UseDescriptionForTitle = true;
        if (dlg.ShowDialog() == DialogResult.OK)
            _pathListBox.Items.Add(dlg.SelectedPath, true);
    }

    private void OnSkipClick(object? sender, EventArgs e) => SkipRequested?.Invoke(this, EventArgs.Empty);
}