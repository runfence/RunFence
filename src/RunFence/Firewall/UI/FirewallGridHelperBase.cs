namespace RunFence.Firewall.UI;

/// <summary>
/// Base class for firewall grid helpers that manage context-menu-driven grids with
/// add/remove/export/edit operations. Handles the shared row-selection, context-menu
/// visibility, Delete key, and export-fallback pattern that is identical across the
/// Internet allowlist and Localhost port grids.
/// </summary>
/// <typeparam name="TEntry">The type stored in <see cref="DataGridViewRow.Tag"/>.</typeparam>
public abstract class FirewallGridHelperBase<TEntry>
    where TEntry : notnull
{
    protected readonly DataGridView Grid;
    private readonly Action<bool> _setAddVisible;
    private readonly Action<bool> _setRemoveVisible;
    private readonly Action<bool> _setExportVisible;
    private readonly Func<IReadOnlyList<string>, string, bool> _tryExportToFile;
    private readonly Action _exportCombined;
    protected readonly Action UpdateApplyButton;
    private int _ctxRowIndex = -1;

    protected FirewallGridHelperBase(
        DataGridView grid,
        Action<bool> setAddVisible,
        Action<bool> setRemoveVisible,
        Action<bool> setExportVisible,
        Func<IReadOnlyList<string>, string, bool> tryExportToFile,
        Action exportCombined,
        Action updateApplyButton)
    {
        Grid = grid;
        _setAddVisible = setAddVisible;
        _setRemoveVisible = setRemoveVisible;
        _setExportVisible = setExportVisible;
        _tryExportToFile = tryExportToFile;
        _exportCombined = exportCombined;
        UpdateApplyButton = updateApplyButton;
    }

    /// <summary>Returns the export string for <paramref name="entry"/>.</summary>
    protected abstract string GetExportValue(TEntry entry);

    /// <summary>The title shown in the export file dialog.</summary>
    protected abstract string ExportTitle { get; }

    /// <summary>Removes <paramref name="entries"/> from the data handler.</summary>
    protected abstract void RemoveEntries(IEnumerable<TEntry> entries);

    /// <summary>
    /// Handles a right-click on the grid: tracks the clicked row index for context-menu use.
    /// </summary>
    public void HandleMouseDown(int x, int y)
    {
        var hit = Grid.HitTest(x, y);
        if (hit.RowIndex >= 0)
        {
            _ctxRowIndex = hit.RowIndex;
            if (!Grid.Rows[hit.RowIndex].Selected)
            {
                Grid.ClearSelection();
                Grid.Rows[hit.RowIndex].Selected = true;
            }
        }
        else
        {
            _ctxRowIndex = -1;
            Grid.ClearSelection();
        }
    }

    /// <summary>
    /// Configures context-menu item visibility based on the last right-clicked row.
    /// </summary>
    public void ConfigureContextMenu()
    {
        _setAddVisible(_ctxRowIndex < 0);
        _setRemoveVisible(_ctxRowIndex >= 0);
        _setExportVisible(_ctxRowIndex >= 0);
    }

    /// <summary>
    /// Handles the Delete key to remove selected rows.
    /// Returns <c>true</c> when the key was handled and the caller should suppress it.
    /// Subclasses may override <see cref="HandleExtraKeys"/> for additional shortcuts.
    /// </summary>
    public bool HandleKeyDown(Keys keyCode, bool control = false)
    {
        if (keyCode == Keys.Delete && Grid.SelectedRows.Count > 0 && !Grid.IsCurrentCellInEditMode)
        {
            RemoveSelected();
            return true;
        }

        return HandleExtraKeys(keyCode, control);
    }

    /// <summary>
    /// Override in subclasses to handle key shortcuts beyond Delete.
    /// Return <c>true</c> when the key was handled.
    /// </summary>
    protected virtual bool HandleExtraKeys(Keys keyCode, bool control) => false;

    /// <summary>
    /// Exports the currently selected entries, or falls back to the combined export when
    /// nothing is selected.
    /// </summary>
    public void ExportSelected()
    {
        var selected = Grid.SelectedRows.Cast<DataGridViewRow>()
            .Where(r => r.Tag is TEntry)
            .Select(r => GetExportValue((TEntry)r.Tag!))
            .ToList();
        if (selected.Count > 0)
            _tryExportToFile(selected, ExportTitle);
        else
            _exportCombined();
    }

    /// <summary>
    /// Removes all currently selected rows from the grid and from the data handler.
    /// </summary>
    public void RemoveSelected()
    {
        if (Grid.SelectedRows.Count == 0)
            return;
        var toRemove = Grid.SelectedRows.Cast<DataGridViewRow>().ToList();
        RemoveEntries(toRemove
            .Where(r => r.Tag is TEntry)
            .Select(r => (TEntry)r.Tag!));
        foreach (var row in toRemove)
            Grid.Rows.Remove(row);
        UpdateApplyButton();
    }

    /// <summary>Validates and applies an in-place cell edit.</summary>
    public abstract void ApplyCellEdit(int rowIndex, int columnIndex);
}
