using RunFence.Account;
using RunFence.Apps.UI;
using RunFence.UI.Forms;

namespace RunFence.Acl.UI.Forms;

/// <summary>
/// Shows the results of an account-wide ACL scan and lets the user select which accounts
/// to import ACL entries for.
/// </summary>
public partial class AclBulkScanResultDialog : Form
{
    private readonly Dictionary<string, AccountScanResult> _results;

    /// <summary>
    /// Gets the subset of scan results for accounts the user chose to import (checked rows).
    /// Only available after the dialog closes with <see cref="DialogResult.OK"/>.
    /// </summary>
    public Dictionary<string, AccountScanResult> SelectedResults { get; private set; } = new();

    public AclBulkScanResultDialog(
        Dictionary<string, AccountScanResult> results,
        ISidNameCacheService sidNameCache)
    {
        _results = results;
        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        DataPanel.ConfigureReadOnlyGrid(_grid);
        _grid.ReadOnly = false;
        PopulateGrid(sidNameCache);
    }

    private void PopulateGrid(ISidNameCacheService sidNameCache)
    {
        _grid.Rows.Clear();

        foreach (var (sid, result) in _results)
        {
            var displayName = sidNameCache.GetDisplayName(sid);
            var idx = _grid.Rows.Add(displayName, result.Grants.Count, result.TraversePaths.Count, true);
            _grid.Rows[idx].Tag = sid;
        }
    }

    private void OnGridCellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0 && e.ColumnIndex == _grid.Columns["Select"]?.Index)
            _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        SelectedResults = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase);

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is string sid &&
                row.Cells["Select"].Value is true &&
                _results.TryGetValue(sid, out var result))
            {
                SelectedResults[sid] = result;
            }
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}