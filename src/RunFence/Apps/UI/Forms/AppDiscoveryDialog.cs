using RunFence.Apps.Shortcuts;
using RunFence.Core.Models;
using RunFence.UI.Forms;

namespace RunFence.Apps.UI.Forms;

public partial class AppDiscoveryDialog : Form
{
    private readonly List<DiscoveredApp> _allApps;
    private readonly Dictionary<string, Image?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    public string? SelectedPath { get; private set; }
    public string? SelectedName { get; private set; }

    public AppDiscoveryDialog(List<DiscoveredApp> apps)
    {
        _allApps = apps;
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        DataPanel.ConfigureReadOnlyGrid(_grid);
        _searchTextBox.TextChanged += (_, _) => ApplyFilter();
        Shown += (_, _) => _searchTextBox.Focus();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = _searchTextBox.Text.Trim();
        _grid.Rows.Clear();

        var filtered = string.IsNullOrEmpty(filter)
            ? _allApps
            : _allApps.Where(a => a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var app in filtered)
        {
            if (!_iconCache.TryGetValue(app.TargetPath, out var icon))
            {
                icon = ShortcutIconHelper.ExtractIcon(app.TargetPath);
                _iconCache[app.TargetPath] = icon;
            }

            var idx = _grid.Rows.Add(icon!, app.Name, app.TargetPath);
            _grid.Rows[idx].Tag = app;
        }

        _emptyLabel.Visible = filtered.Count == 0;
        _grid.Visible = filtered.Count > 0;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        SelectCurrent();
    }

    private void OnGridDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0)
            SelectCurrent();
    }

    private void SelectCurrent()
    {
        if (_grid.SelectedRows.Count == 0)
            return;
        if (_grid.SelectedRows[0].Tag is not DiscoveredApp app)
            return;

        SelectedPath = app.TargetPath;
        SelectedName = app.Name;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void DisposeIconCache()
    {
        foreach (var icon in _iconCache.Values)
            icon?.Dispose();
        _iconCache.Clear();
    }
}