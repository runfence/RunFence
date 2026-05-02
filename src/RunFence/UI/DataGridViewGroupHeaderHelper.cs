namespace RunFence.UI;

/// <summary>
/// Suppresses cell tooltips (including auto-truncation tooltips) on group/section header rows
/// in a DataGridView, identified by their row <see cref="DataGridViewRow.Tag"/> type.
/// </summary>
public static class DataGridViewGroupHeaderHelper
{
    /// <summary>
    /// Subscribes to <see cref="DataGridView.CellMouseEnter"/> and sets
    /// <see cref="DataGridView.ShowCellToolTips"/> to <c>false</c> whenever the mouse is on a
    /// row tagged with <typeparamref name="T"/>, and restores it to <c>true</c> for all other
    /// rows (including column-header cells with <c>RowIndex == -1</c>).
    /// </summary>
    public static void SuppressGroupHeaderTooltips<T>(DataGridView grid)
    {
        grid.CellMouseEnter += (_, e) =>
        {
            if (e.RowIndex < 0)
            {
                grid.ShowCellToolTips = true;
                return;
            }
            if (e.RowIndex < grid.Rows.Count)
                grid.ShowCellToolTips = grid.Rows[e.RowIndex].Tag is not T;
        };
    }
}
