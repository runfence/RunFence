namespace RunFence.Firewall.UI;

/// <summary>
/// Manages the Localhost-tab grid UI operations for <see cref="Forms.FirewallAllowlistDialog"/>:
/// populating, adding, removing, editing, and exporting port entries in the grid.
/// Event handlers in the dialog extract parameters from UI state and delegate to these methods.
/// </summary>
public class FirewallPortsGridHelper(
    DataGridView portsGrid,
    FirewallPortsTabHandler handler,
    Func<IReadOnlyList<string>, string, bool> tryExportToFile,
    Action exportCombined,
    Action updateApplyButton,
    Action<string, string> showInformation,
    Action<string, string> showWarning)
    : FirewallGridHelperBase<string>(
        portsGrid,
        tryExportToFile,
        exportCombined,
        updateApplyButton,
        FirewallAllowlistContextMenuItemNames.PortsAdd,
        FirewallAllowlistContextMenuItemNames.PortsRemove,
        FirewallAllowlistContextMenuItemNames.PortsExport)
{
    protected override string GetExportValue(string entry) => $"localhost:{entry}";

    protected override string ExportTitle => "Export Port Exceptions";

    protected override void RemoveEntries(IEnumerable<string> entries) =>
        handler.RemovePorts(entries);

    public void PopulatePortsGrid()
    {
        Grid.Rows.Clear();
        foreach (var entry in handler.GetPortEntries())
            AddPortRow(entry);
    }

    /// <summary>
    /// Validates <paramref name="input"/>, adds the port entry to the handler's list, and adds a grid row.
    /// Shows validation/duplicate/limit feedback via <see cref="MessageBox"/>.
    /// </summary>
    public void AddPort(string input)
    {
        var result = handler.AddPort(input);
        switch (result.Outcome)
        {
            case AddPortOutcome.LimitReached:
                showInformation("Limit Reached", $"Maximum of {LocalhostPortParser.MaxAllowedPorts} port entries reached.");
                return;
            case AddPortOutcome.Invalid:
                showWarning("Validation Error", "Invalid port entry. Enter a port (1\u201365535) or range (e.g. 8080-8090).");
                return;
            case AddPortOutcome.Duplicate:
                showWarning("Duplicate", "This entry is already in the list.");
                return;
        }

        AddPortRow(result.Entry!);
        UpdateApplyButton();
    }

    /// <summary>
    /// Validates and applies an in-place cell edit for a port entry.
    /// </summary>
    public override void ApplyCellEdit(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0)
            return;
        var row = Grid.Rows[rowIndex];
        var oldValue = row.Tag as string;
        var newValue = (row.Cells[columnIndex].Value as string)?.Trim() ?? "";

        if (string.Equals(newValue, oldValue, StringComparison.OrdinalIgnoreCase))
            return;

        var result = handler.ValidateEdit(oldValue, newValue);
        switch (result.Outcome)
        {
            case EditPortOutcome.Invalid:
                showWarning("Validation Error", "Invalid port entry. Enter a port (1\u201365535) or range (e.g. 8080-8090).");
                row.Cells[columnIndex].Value = oldValue;
                return;
            case EditPortOutcome.Duplicate:
                showWarning("Duplicate", "This entry is already in the list.");
                row.Cells[columnIndex].Value = oldValue;
                return;
        }

        row.Tag = result.NormalizedValue;
        row.Cells[columnIndex].Value = result.NormalizedValue;
        UpdateApplyButton();
    }

    /// <summary>
    /// Adds grid rows for all <paramref name="ports"/>.
    /// Call after the ports have been added to the data handler.
    /// </summary>
    public void AddImportedPorts(IReadOnlyList<string> ports)
    {
        foreach (var port in ports)
            AddPortRow(port);
        if (ports.Count > 0)
            UpdateApplyButton();
    }

    private void AddPortRow(string port)
    {
        var idx = Grid.Rows.Add(port);
        Grid.Rows[idx].Tag = port;
    }
}
