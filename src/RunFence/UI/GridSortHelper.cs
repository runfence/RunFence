using System.ComponentModel;

namespace RunFence.UI;

/// <summary>
/// Manages three-state column sorting for a DataGridView.
/// Extracted from DataPanel to allow reuse and reduce its size.
/// </summary>
public class GridSortHelper
{
    /// <summary>True when a column sort is active (Ascending or Descending).</summary>
    public bool IsSortActive => SortColumnIndex >= 0;

    /// <summary>The index of the currently sorted column, or -1 if no sort is active.</summary>
    public int SortColumnIndex { get; private set; } = -1;

    /// <summary>The current sort direction.</summary>
    public SortOrder SortDirection { get; private set; } = SortOrder.None;

    /// <summary>
    /// Attach to a grid's ColumnHeaderMouseClick to enable Asc→Desc→None cycling.
    /// When direction returns to None, calls <paramref name="restoreOriginalOrder"/>.
    /// <para>When <paramref name="sectioned"/> is true, the grid contains section header rows
    /// that must not be sorted by DataGridView. Instead of calling grid.Sort(), the handler
    /// calls <paramref name="restoreOriginalOrder"/> for all state transitions (including sorted
    /// states). The rebuild method reads <see cref="SortColumnIndex"/>/<see cref="SortDirection"/>
    /// to sort within sections and calls <see cref="ReapplyGlyphIfActive"/> for the glyph.</para>
    /// <para>When <paramref name="sectioned"/> is false (default), <paramref name="onEnterSort"/>
    /// (optional) is called when transitioning from unsorted to sorted mode — use to rebuild
    /// grouped displays into flat rows before DataGridView's built-in sort runs.</para>
    /// </summary>
    public void EnableThreeStateSorting(DataGridView grid, Action restoreOriginalOrder,
        Action? onEnterSort = null, bool sectioned = false)
    {
        // Switch from Automatic to Programmatic so the built-in sort doesn't
        // fire before our handler — we fully control the sort lifecycle.
        foreach (DataGridViewColumn c in grid.Columns)
        {
            if (c.SortMode == DataGridViewColumnSortMode.Automatic)
                c.SortMode = DataGridViewColumnSortMode.Programmatic;
        }

        grid.ColumnHeaderMouseClick += (_, e) =>
        {
            var col = grid.Columns[e.ColumnIndex];
            if (col.SortMode == DataGridViewColumnSortMode.NotSortable)
                return;

            bool enteringSort = SortColumnIndex < 0; // transitioning from unsorted/grouped mode

            if (e.ColumnIndex != SortColumnIndex)
            {
                SortColumnIndex = e.ColumnIndex;
                SortDirection = SortOrder.Ascending;
            }
            else
            {
                SortDirection = SortDirection switch
                {
                    SortOrder.Ascending => SortOrder.Descending,
                    SortOrder.Descending => SortOrder.None,
                    _ => SortOrder.Ascending
                };
            }

            if (SortDirection == SortOrder.None)
            {
                SortColumnIndex = -1;
                foreach (DataGridViewColumn c in grid.Columns)
                    c.HeaderCell.SortGlyphDirection = SortOrder.None;
                restoreOriginalOrder();
            }
            else if (sectioned)
            {
                // Sectioned mode: rebuild in-place with sorted sections (no grid.Sort — it would
                // mix section header rows with data rows). Glyph is set by ReapplyGlyphIfActive
                // inside restoreOriginalOrder → RefreshGrid.
                foreach (DataGridViewColumn c in grid.Columns)
                    c.HeaderCell.SortGlyphDirection = SortOrder.None;
                restoreOriginalOrder();
            }
            else
            {
                // If entering sort from grouped/unsorted mode, let the panel rebuild
                // its rows in flat order first so header rows don't get sorted in.
                if (enteringSort)
                    onEnterSort?.Invoke();

                foreach (DataGridViewColumn c in grid.Columns)
                    c.HeaderCell.SortGlyphDirection = SortOrder.None;

                var direction = SortDirection == SortOrder.Ascending
                    ? ListSortDirection.Ascending
                    : ListSortDirection.Descending;
                grid.Sort(col, direction);
                col.HeaderCell.SortGlyphDirection = SortDirection;
            }
        };
    }

    /// <summary>
    /// Re-applies the current sort after a grid repopulation (e.g. after add/edit/remove).
    /// No-op when no sort is active.
    /// </summary>
    public void ReapplySortIfActive(DataGridView grid)
    {
        if (SortColumnIndex >= 0 && SortColumnIndex < grid.Columns.Count
                                 && SortDirection != SortOrder.None)
        {
            var col = grid.Columns[SortColumnIndex];
            var direction = SortDirection == SortOrder.Ascending
                ? ListSortDirection.Ascending
                : ListSortDirection.Descending;
            grid.Sort(col, direction);
            col.HeaderCell.SortGlyphDirection = SortDirection;
        }
    }

    /// <summary>
    /// Re-applies the sort glyph after a sectioned grid repopulation without calling grid.Sort().
    /// Used by sectioned panels that sort within sections manually.
    /// No-op when no sort is active.
    /// </summary>
    public void ReapplyGlyphIfActive(DataGridView grid)
    {
        if (SortColumnIndex >= 0 && SortColumnIndex < grid.Columns.Count
                                 && SortDirection != SortOrder.None)
        {
            foreach (DataGridViewColumn c in grid.Columns)
                c.HeaderCell.SortGlyphDirection = SortOrder.None;
            grid.Columns[SortColumnIndex].HeaderCell.SortGlyphDirection = SortDirection;
        }
    }

    /// <summary>
    /// Sorts <paramref name="items"/> by <paramref name="keySelector"/> using the current
    /// sort direction. Used by sectioned panels to sort within each section.
    /// </summary>
    public IOrderedEnumerable<T> SortByActiveColumn<T>(
        IEnumerable<T> items, Func<T, string> keySelector)
        => SortDirection == SortOrder.Descending
            ? items.OrderByDescending(keySelector, StringComparer.OrdinalIgnoreCase)
            : items.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase);
}