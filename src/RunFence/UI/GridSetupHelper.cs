namespace RunFence.UI;

/// <summary>
/// Static utility methods for configuring and navigating DataGridView controls.
/// Extracted from DataPanel to be reusable outside of DataPanel subclasses.
/// </summary>
public static class GridSetupHelper
{
    /// <summary>
    /// Selects the first row in the grid and moves focus to its first non-image cell.
    /// </summary>
    public static void SelectFirstRow(DataGridView grid)
    {
        if (grid.Rows.Count > 0)
        {
            grid.Rows[0].Selected = true;
            grid.CurrentCell = grid.Rows[0].Cells[GetFirstTextCellIndex(grid)];
        }
    }

    /// <summary>
    /// Selects the row at <paramref name="index"/> (clamped to last row) and moves focus to its first non-image cell.
    /// </summary>
    public static void SelectRowByIndex(DataGridView grid, int index)
    {
        if (grid.Rows.Count == 0)
            return;
        var targetIndex = Math.Min(index, grid.Rows.Count - 1);
        grid.Rows[targetIndex].Selected = true;
        grid.CurrentCell = grid.Rows[targetIndex].Cells[GetFirstTextCellIndex(grid)];
    }

    /// <summary>
    /// Shows <paramref name="menu"/> centered over the selected row in <paramref name="grid"/>.
    /// </summary>
    public static void ShowContextMenuAtRow(DataGridView grid, ContextMenuStrip menu)
    {
        if (grid.SelectedRows.Count == 0)
            return;
        var row = grid.SelectedRows[0];
        var cellBounds = grid.GetCellDisplayRectangle(1, row.Index, true);
        menu.Show(grid, new Point(cellBounds.Left + cellBounds.Width / 2, cellBounds.Top + cellBounds.Height / 2));
    }

    /// <summary>
    /// Handles a right-click on a grid row: selects the row and shows <paramref name="contextMenu"/> at the cursor.
    /// </summary>
    public static void HandleRightClickRowSelect(DataGridView grid, DataGridViewCellMouseEventArgs e, ContextMenuStrip contextMenu)
    {
        if (e.Button != MouseButtons.Right || e.RowIndex < 0)
            return;
        grid.ClearSelection();
        grid.Rows[e.RowIndex].Selected = true;
        grid.CurrentCell = grid.Rows[e.RowIndex].Cells[GetFirstTextCellIndex(grid)];
        contextMenu.Show(grid, grid.PointToClient(Cursor.Position));
    }

    /// <summary>
    /// Returns the index of the first non-image column cell suitable for CurrentCell assignment.
    /// </summary>
    private static int GetFirstTextCellIndex(DataGridView grid)
    {
        for (int i = 0; i < grid.Columns.Count; i++)
        {
            if (grid.Columns[i] is not DataGridViewImageColumn && grid.Columns[i].Visible)
                return i;
        }

        return 0;
    }
}
