namespace RunFence.UI;

/// <summary>
/// Utility for improving DataGridView combo-box interaction.
/// </summary>
public static class DataGridViewComboHelper
{
    /// <summary>
    /// Makes combo-box cells open on the first click instead of requiring two clicks.
    /// <para>
    /// First click calls <see cref="DataGridView.BeginEdit"/> so the cell enters edit mode
    /// immediately. A deferred invocation then sets <see cref="ComboBox.DroppedDown"/> = true
    /// so the dropdown list opens without a second click.
    /// </para>
    /// </summary>
    /// <param name="grid">The grid to configure.</param>
    /// <param name="shouldOpenDropDown">
    /// Optional filter: receives the column index and returns true when the dropdown should be
    /// opened automatically. When null, all <see cref="DataGridViewComboBoxColumn"/> columns
    /// open on first click. Pass a predicate to skip columns with <see cref="ComboBoxStyle.DropDown"/>
    /// (editable) style where auto-open would be disruptive.
    /// </param>
    public static void EnableComboOpenOnFirstClick(DataGridView grid, Func<int, bool>? shouldOpenDropDown = null)
    {
        grid.CellClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && grid.Columns[e.ColumnIndex] is DataGridViewComboBoxColumn)
                grid.BeginEdit(true);
        };

        grid.EditingControlShowing += (_, e) =>
        {
            if (e.Control is not ComboBox combo)
                return;
            if (grid.CurrentCell == null)
                return;
            int col = grid.CurrentCell.ColumnIndex;
            if (grid.Columns[col] is not DataGridViewComboBoxColumn)
                return;
            if (shouldOpenDropDown != null && !shouldOpenDropDown(col))
                return;
            grid.BeginInvoke(() =>
            {
                if (!combo.IsDisposed)
                    combo.DroppedDown = true;
            });
        };
    }
}