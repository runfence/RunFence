namespace RunFence.Firewall.UI;

/// <summary>
/// Manages the Localhost-tab grid UI operations for <see cref="Forms.FirewallAllowlistDialog"/>:
/// populating, adding, removing, editing, and exporting port entries in the grid.
/// Event handlers in the dialog extract parameters from UI state and delegate to these methods.
/// </summary>
public class FirewallPortsGridHelper
{
    private readonly DataGridView _portsGrid;
    private readonly ToolStripMenuItem _portsCtxAdd;
    private readonly ToolStripMenuItem _portsCtxRemove;
    private readonly ToolStripMenuItem _portsCtxExport;
    private readonly FirewallPortsTabHandler _handler;
    private readonly Func<IReadOnlyList<string>, string, bool> _tryExportToFile;
    private readonly Action _exportCombined;
    private readonly Action _updateApplyButton;
    private int _portsCtxRowIndex = -1;

    public FirewallPortsGridHelper(
        DataGridView portsGrid,
        ToolStripMenuItem portsCtxAdd,
        ToolStripMenuItem portsCtxRemove,
        ToolStripMenuItem portsCtxExport,
        FirewallPortsTabHandler handler,
        Func<IReadOnlyList<string>, string, bool> tryExportToFile,
        Action exportCombined,
        Action updateApplyButton)
    {
        _portsGrid = portsGrid;
        _portsCtxAdd = portsCtxAdd;
        _portsCtxRemove = portsCtxRemove;
        _portsCtxExport = portsCtxExport;
        _handler = handler;
        _tryExportToFile = tryExportToFile;
        _exportCombined = exportCombined;
        _updateApplyButton = updateApplyButton;
    }

    public void PopulatePortsGrid()
    {
        _portsGrid.Rows.Clear();
        foreach (var entry in _handler.GetPortEntries())
            AddPortRow(entry);
    }

    /// <summary>
    /// Validates <paramref name="input"/>, adds the port entry to the handler's list, and adds a grid row.
    /// Shows validation/duplicate/limit feedback via <see cref="MessageBox"/>.
    /// </summary>
    public void AddPort(string input)
    {
        var result = _handler.AddPort(input);
        switch (result.Outcome)
        {
            case AddPortOutcome.LimitReached:
                MessageBox.Show($"Maximum of {LocalhostPortParser.MaxAllowedPorts} port entries reached.",
                    "Limit Reached", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            case AddPortOutcome.Invalid:
                MessageBox.Show("Invalid port entry. Enter a port (1\u201365535) or range (e.g. 8080-8090).",
                    "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            case AddPortOutcome.Duplicate:
                MessageBox.Show("This entry is already in the list.", "Duplicate",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
        }

        AddPortRow(result.Entry!);
        _updateApplyButton();
    }

    /// <summary>
    /// Removes all currently selected rows from the ports grid and the handler's entry list.
    /// </summary>
    public void RemoveSelectedPorts()
    {
        if (_portsGrid.SelectedRows.Count == 0)
            return;
        var toRemove = _portsGrid.SelectedRows.Cast<DataGridViewRow>().ToList();
        _handler.RemovePorts(toRemove
            .Where(r => r.Tag is string)
            .Select(r => (string)r.Tag!));
        foreach (var row in toRemove)
            _portsGrid.Rows.Remove(row);
        _updateApplyButton();
    }

    /// <summary>
    /// Exports the currently selected ports, or falls back to the combined export when
    /// nothing is selected.
    /// </summary>
    public void ExportSelected()
    {
        var selected = _portsGrid.SelectedRows.Cast<DataGridViewRow>()
            .Where(r => r.Tag is string)
            .Select(r => (string)r.Tag!)
            .ToList();
        if (selected.Count > 0)
            _tryExportToFile(selected.Select(p => $"localhost:{p}").ToList(), "Export Port Exceptions");
        else
            _exportCombined();
    }

    /// <summary>
    /// Handles a right-click on the ports grid: tracks the clicked row for context menu use.
    /// </summary>
    public void HandleMouseDown(int x, int y)
    {
        var hit = _portsGrid.HitTest(x, y);
        if (hit.RowIndex >= 0)
        {
            _portsCtxRowIndex = hit.RowIndex;
            if (!_portsGrid.Rows[hit.RowIndex].Selected)
            {
                _portsGrid.ClearSelection();
                _portsGrid.Rows[hit.RowIndex].Selected = true;
            }
        }
        else
        {
            _portsCtxRowIndex = -1;
            _portsGrid.ClearSelection();
        }
    }

    /// <summary>
    /// Configures context menu item visibility based on the last right-clicked row.
    /// </summary>
    public void ConfigureContextMenu()
    {
        _portsCtxAdd.Visible = _portsCtxRowIndex < 0;
        _portsCtxRemove.Visible = _portsCtxRowIndex >= 0;
        _portsCtxExport.Visible = _portsCtxRowIndex >= 0;
    }

    /// <summary>
    /// Handles Delete key to remove selected ports.
    /// Returns <c>true</c> when the key was handled and the caller should suppress it.
    /// </summary>
    public bool HandleKeyDown(Keys keyCode)
    {
        if (keyCode == Keys.Delete && _portsGrid.SelectedRows.Count > 0 && !_portsGrid.IsCurrentCellInEditMode)
        {
            RemoveSelectedPorts();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Validates and applies an in-place cell edit for a port entry.
    /// </summary>
    public void ApplyCellEdit(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0)
            return;
        var row = _portsGrid.Rows[rowIndex];
        var oldValue = row.Tag as string;
        var newValue = (row.Cells[columnIndex].Value as string)?.Trim() ?? "";

        if (string.Equals(newValue, oldValue, StringComparison.OrdinalIgnoreCase))
            return;

        var result = _handler.ValidateEdit(oldValue, newValue);
        switch (result.Outcome)
        {
            case EditPortOutcome.Invalid:
                MessageBox.Show("Invalid port entry. Enter a port (1\u201365535) or range (e.g. 8080-8090).",
                    "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                row.Cells[columnIndex].Value = oldValue;
                return;
            case EditPortOutcome.Duplicate:
                MessageBox.Show("This entry is already in the list.", "Duplicate",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                row.Cells[columnIndex].Value = oldValue;
                return;
        }

        row.Tag = result.NormalizedValue;
        row.Cells[columnIndex].Value = result.NormalizedValue;
        _updateApplyButton();
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
            _updateApplyButton();
    }

    private void AddPortRow(string port)
    {
        var idx = _portsGrid.Rows.Add(port);
        _portsGrid.Rows[idx].Tag = port;
    }
}
