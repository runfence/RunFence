using RunFence.Core.Models;

namespace RunFence.Firewall.UI;

/// <summary>
/// Manages the Internet-tab grid UI operations for <see cref="Forms.FirewallAllowlistDialog"/>:
/// populating, adding, removing, editing, exporting, and resolving domain entries in the grid.
/// Event handlers in the dialog extract parameters from UI state and delegate to these methods.
/// </summary>
public class FirewallAllowlistGridHelper
{
    private readonly DataGridView _grid;
    private readonly ToolStripMenuItem _ctxAdd;
    private readonly ToolStripMenuItem _ctxRemoveItem;
    private readonly ToolStripMenuItem _ctxExportItem;
    private readonly FirewallAllowlistTabHandler _handler;
    private readonly Func<IReadOnlyList<string>, string, bool> _tryExportToFile;
    private readonly Action _exportCombined;
    private readonly Action _updateApplyButton;
    private readonly Action _updateToolbar;
    private int _ctxRowIndex = -1;

    public bool IsResolvingDomains => _handler.IsResolvingDomains;

    public FirewallAllowlistGridHelper(
        DataGridView grid,
        ToolStripMenuItem ctxAdd,
        ToolStripMenuItem ctxRemoveItem,
        ToolStripMenuItem ctxExportItem,
        FirewallAllowlistTabHandler handler,
        Func<IReadOnlyList<string>, string, bool> tryExportToFile,
        Action exportCombined,
        Action updateApplyButton,
        Action updateToolbar)
    {
        _grid = grid;
        _ctxAdd = ctxAdd;
        _ctxRemoveItem = ctxRemoveItem;
        _ctxExportItem = ctxExportItem;
        _handler = handler;
        _tryExportToFile = tryExportToFile;
        _exportCombined = exportCombined;
        _updateApplyButton = updateApplyButton;
        _updateToolbar = updateToolbar;
    }

    public void PopulateGrid()
    {
        _grid.Rows.Clear();
        foreach (var entry in _handler.GetEntries())
            AddRow(entry);
    }

    /// <summary>
    /// Adds grid rows for all <paramref name="entries"/> and starts background domain resolution
    /// for any domain entries. Call after the entries have been added to the data handler.
    /// </summary>
    public void AddImportedEntries(IReadOnlyList<FirewallAllowlistEntry> entries)
    {
        foreach (var entry in entries)
        {
            var row = AddRow(entry);
            if (entry.IsDomain)
                ResolveEntryAsync(entry, row);
        }
        if (entries.Count > 0)
            _updateApplyButton();
    }

    /// <summary>
    /// Validates <paramref name="input"/>, adds the entry to the handler's list, and adds a grid row.
    /// Shows validation/duplicate/license-limit feedback via <see cref="MessageBox"/>.
    /// </summary>
    public void AddEntry(string input)
    {
        var result = _handler.AddEntry(input);
        switch (result.Outcome)
        {
            case AddEntryOutcome.LicenseLimitReached:
                MessageBox.Show(result.LicenseLimitMessage, "License Limit",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            case AddEntryOutcome.Invalid:
                MessageBox.Show(
                    "Invalid entry. Enter a valid IP address or CIDR range (e.g. 1.2.3.4, 10.0.0.0/8) or domain name (e.g. example.com).",
                    "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            case AddEntryOutcome.Duplicate:
                MessageBox.Show("This entry is already in the list.", "Duplicate",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
        }

        var row = AddRow(result.Entry!);
        if (result.Entry!.IsDomain)
            ResolveEntryAsync(result.Entry, row);
        _updateApplyButton();
    }

    /// <summary>
    /// Removes all currently selected rows from the grid and the handler's entry list.
    /// </summary>
    public void RemoveSelectedEntries()
    {
        if (_grid.SelectedRows.Count == 0)
            return;
        var toRemove = _grid.SelectedRows.Cast<DataGridViewRow>().ToList();
        _handler.RemoveEntries(toRemove
            .Where(r => r.Tag is FirewallAllowlistEntry)
            .Select(r => (FirewallAllowlistEntry)r.Tag!));
        foreach (var row in toRemove)
            _grid.Rows.Remove(row);
        _updateApplyButton();
    }

    /// <summary>
    /// Exports the currently selected entries, or falls back to the combined export when
    /// nothing is selected.
    /// </summary>
    public void ExportSelected()
    {
        var selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Where(r => r.Tag is FirewallAllowlistEntry)
            .Select(r => ((FirewallAllowlistEntry)r.Tag!).Value)
            .ToList();
        if (selected.Count > 0)
            _tryExportToFile(selected, "Export Firewall Allowlist");
        else
            _exportCombined();
    }

    /// <summary>
    /// Adds entries returned from the blocked connections dialog.
    /// Returns a <see cref="BlockedConnectionAddResult"/> with added entries and truncated count.
    /// </summary>
    public BlockedConnectionAddResult AddEntriesFromBlockedConnections(IEnumerable<FirewallAllowlistEntry> selected)
    {
        var result = _handler.AddEntriesFromBlockedConnections(selected);
        foreach (var entry in result.Added)
        {
            var row = AddRow(entry);
            if (entry.IsDomain)
                ResolveEntryAsync(entry, row);
        }
        if (result.Added.Count > 0)
            _updateApplyButton();
        return result;
    }

    private DataGridViewRow AddRow(FirewallAllowlistEntry entry)
    {
        var typeText = entry.IsDomain ? "Domain" : "IP/CIDR";
        var resolvedText = entry.IsDomain ? "" : entry.Value;
        var idx = _grid.Rows.Add(typeText, entry.Value, resolvedText);
        _grid.Rows[idx].Tag = entry;
        return _grid.Rows[idx];
    }

    public async Task ResolveDomainEntriesAsync(bool showError)
    {
        SetDomainRowsResolvedText("Resolving...");
        var resolveTask = _handler.ResolveAllDomainsAsync();
        // ResolveAllDomainsAsync sets IsResolvingDomains = true synchronously before yielding,
        // so UpdateToolbarForCurrentTab correctly disables the add/resolve buttons.
        _updateToolbar();

        try
        {
            var resolved = await resolveTask;
            UpdateResolvedColumn(resolved);
        }
        catch (Exception ex)
        {
            if (showError)
                MessageBox.Show($"Resolution failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetDomainRowsResolvedText("(unresolved)");
        }
        finally
        {
            _updateToolbar();
        }
    }

    private async void ResolveEntryAsync(FirewallAllowlistEntry entry, DataGridViewRow row)
    {
        row.Cells["Resolved"].Value = "Resolving...";
        try
        {
            var ips = await _handler.ResolveEntryAsync(entry);
            if (row.DataGridView == null)
                return;
            row.Cells["Resolved"].Value = ips.Count > 0
                ? string.Join(", ", ips)
                : "(no addresses)";
        }
        catch
        {
            if (row.DataGridView == null)
                return;
            row.Cells["Resolved"].Value = "(unresolved)";
        }
    }

    /// <summary>
    /// Handles a right-click on the grid: tracks the clicked row for context menu use.
    /// </summary>
    public void HandleMouseDown(int x, int y)
    {
        var hit = _grid.HitTest(x, y);
        if (hit.RowIndex >= 0)
        {
            _ctxRowIndex = hit.RowIndex;
            if (!_grid.Rows[hit.RowIndex].Selected)
            {
                _grid.ClearSelection();
                _grid.Rows[hit.RowIndex].Selected = true;
            }
        }
        else
        {
            _ctxRowIndex = -1;
            _grid.ClearSelection();
        }
    }

    /// <summary>
    /// Configures context menu item visibility based on the last right-clicked row.
    /// </summary>
    public void ConfigureContextMenu()
    {
        _ctxAdd.Visible = _ctxRowIndex < 0;
        _ctxRemoveItem.Visible = _ctxRowIndex >= 0;
        _ctxExportItem.Visible = _ctxRowIndex >= 0;
    }

    /// <summary>
    /// Handles Delete (remove) and Ctrl+C (copy to clipboard) keyboard shortcuts.
    /// Returns <c>true</c> when the key was handled and the caller should suppress it.
    /// </summary>
    public bool HandleKeyDown(Keys keyCode, bool control)
    {
        if (keyCode == Keys.Delete && _grid.SelectedRows.Count > 0 && !_grid.IsCurrentCellInEditMode)
        {
            RemoveSelectedEntries();
            return true;
        }

        if (keyCode == Keys.C && control && _grid.SelectedRows.Count > 0 && !_grid.IsCurrentCellInEditMode)
        {
            var values = _grid.SelectedRows.Cast<DataGridViewRow>()
                .Where(r => r.Tag is FirewallAllowlistEntry)
                .Select(r => ((FirewallAllowlistEntry)r.Tag!).Value)
                .ToList();
            if (values.Count > 0)
            {
                Clipboard.SetText(string.Join(Environment.NewLine, values));
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Validates and applies an in-place cell edit for the Value column.
    /// </summary>
    public void ApplyCellEdit(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0)
            return;
        if (_grid.Columns[columnIndex].Name != "Value")
            return;
        var row = _grid.Rows[rowIndex];
        if (row.Tag is not FirewallAllowlistEntry entry)
            return;

        var newValue = (row.Cells[columnIndex].Value as string)?.Trim() ?? "";
        if (string.Equals(newValue, entry.Value, StringComparison.OrdinalIgnoreCase))
            return;

        var result = _handler.ValidateEdit(entry, newValue);
        switch (result.Outcome)
        {
            case EditEntryOutcome.Invalid:
                MessageBox.Show(
                    "Invalid entry. Enter a valid IP address or CIDR range (e.g. 1.2.3.4, 10.0.0.0/8) or domain name (e.g. example.com).",
                    "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                row.Cells[columnIndex].Value = entry.Value;
                return;
            case EditEntryOutcome.Duplicate:
                MessageBox.Show("This value is already in the list.", "Duplicate",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                row.Cells[columnIndex].Value = entry.Value;
                return;
        }

        row.Cells[columnIndex].Value = newValue;
        row.Cells["Type"].Value = result.UpdatedEntry!.IsDomain ? "Domain" : "IP/CIDR";
        if (result.UpdatedEntry.IsDomain)
            ResolveEntryAsync(entry, row);
        else
            row.Cells["Resolved"].Value = newValue;
        _updateApplyButton();
    }

    private void SetDomainRowsResolvedText(string text)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is FirewallAllowlistEntry { IsDomain: true })
                row.Cells["Resolved"].Value = text;
        }
    }

    private void UpdateResolvedColumn(Dictionary<string, List<string>> resolved)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not FirewallAllowlistEntry entry || !entry.IsDomain)
                continue;
            row.Cells["Resolved"].Value = resolved.TryGetValue(entry.Value, out var ips) && ips.Count > 0
                ? string.Join(", ", ips)
                : "(no addresses)";
        }
    }
}
