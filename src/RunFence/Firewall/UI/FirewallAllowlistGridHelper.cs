using RunFence.Core.Models;

namespace RunFence.Firewall.UI;

/// <summary>
/// Manages the Internet-tab grid UI operations for <see cref="Forms.FirewallAllowlistDialog"/>:
/// populating, adding, removing, editing, exporting, and resolving domain entries in the grid.
/// Event handlers in the dialog extract parameters from UI state and delegate to these methods.
/// </summary>
public class FirewallAllowlistGridHelper(
    DataGridView grid,
    Action<bool> setAddVisible,
    Action<bool> setRemoveVisible,
    Action<bool> setExportVisible,
    FirewallAllowlistTabHandler handler,
    Func<IReadOnlyList<string>, string, bool> tryExportToFile,
    Action exportCombined,
    Action updateApplyButton,
    Action updateToolbar)
    : FirewallGridHelperBase<FirewallAllowlistEntry>(
        grid, setAddVisible, setRemoveVisible, setExportVisible,
        tryExportToFile, exportCombined, updateApplyButton)
{
    public bool IsResolvingDomains => handler.IsResolvingDomains;

    protected override string GetExportValue(FirewallAllowlistEntry entry) => entry.Value;

    protected override string ExportTitle => "Export Firewall Allowlist";

    protected override void RemoveEntries(IEnumerable<FirewallAllowlistEntry> entries) =>
        handler.RemoveEntries(entries);

    protected override bool HandleExtraKeys(Keys keyCode, bool control)
    {
        if (keyCode == Keys.C && control && Grid.SelectedRows.Count > 0 && !Grid.IsCurrentCellInEditMode)
        {
            var values = Grid.SelectedRows.Cast<DataGridViewRow>()
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

    public void PopulateGrid()
    {
        Grid.Rows.Clear();
        foreach (var entry in handler.GetEntries())
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
            UpdateApplyButton();
    }

    /// <summary>
    /// Validates <paramref name="input"/>, adds the entry to the handler's list, and adds a grid row.
    /// Shows validation/duplicate/license-limit feedback via <see cref="MessageBox"/>.
    /// </summary>
    public void AddEntry(string input)
    {
        var result = handler.AddEntry(input);
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
        UpdateApplyButton();
    }

    /// <summary>
    /// Adds entries returned from the blocked connections dialog.
    /// Returns a <see cref="BlockedConnectionAddResult"/> with added entries and truncated count.
    /// </summary>
    public BlockedConnectionAddResult AddEntriesFromBlockedConnections(IEnumerable<FirewallAllowlistEntry> selected)
    {
        var result = handler.AddEntriesFromBlockedConnections(selected);
        foreach (var entry in result.Added)
        {
            var row = AddRow(entry);
            if (entry.IsDomain)
                ResolveEntryAsync(entry, row);
        }
        if (result.Added.Count > 0)
            UpdateApplyButton();
        return result;
    }

    private DataGridViewRow AddRow(FirewallAllowlistEntry entry)
    {
        var typeText = entry.IsDomain ? "Domain" : "IP/CIDR";
        var resolvedText = entry.IsDomain ? "" : entry.Value;
        var idx = Grid.Rows.Add(typeText, entry.Value, resolvedText);
        Grid.Rows[idx].Tag = entry;
        return Grid.Rows[idx];
    }

    public async Task ResolveDomainEntriesAsync(bool showError)
    {
        SetDomainRowsResolvedText("Resolving...");
        var resolveTask = handler.ResolveAllDomainsAsync();
        // ResolveAllDomainsAsync sets IsResolvingDomains = true synchronously before yielding,
        // so UpdateToolbarForCurrentTab correctly disables the add/resolve buttons.
        updateToolbar();

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
            updateToolbar();
        }
    }

    private async void ResolveEntryAsync(FirewallAllowlistEntry entry, DataGridViewRow row)
    {
        row.Cells["Resolved"].Value = "Resolving...";
        try
        {
            var ips = await handler.ResolveEntryAsync(entry);
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
    /// Validates and applies an in-place cell edit for the Value column.
    /// </summary>
    public override void ApplyCellEdit(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0)
            return;
        if (Grid.Columns[columnIndex].Name != "Value")
            return;
        var row = Grid.Rows[rowIndex];
        if (row.Tag is not FirewallAllowlistEntry entry)
            return;

        var newValue = (row.Cells[columnIndex].Value as string)?.Trim() ?? "";
        if (string.Equals(newValue, entry.Value, StringComparison.OrdinalIgnoreCase))
            return;

        var result = handler.ValidateEdit(entry, newValue);
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
        UpdateApplyButton();
    }

    private void SetDomainRowsResolvedText(string text)
    {
        foreach (DataGridViewRow row in Grid.Rows)
        {
            if (row.Tag is FirewallAllowlistEntry { IsDomain: true })
                row.Cells["Resolved"].Value = text;
        }
    }

    private void UpdateResolvedColumn(Dictionary<string, List<string>> resolved)
    {
        foreach (DataGridViewRow row in Grid.Rows)
        {
            if (row.Tag is not FirewallAllowlistEntry { IsDomain: true } entry)
                continue;
            row.Cells["Resolved"].Value = resolved.TryGetValue(entry.Value, out var ips) && ips.Count > 0
                ? string.Join(", ", ips)
                : "(no addresses)";
        }
    }
}
