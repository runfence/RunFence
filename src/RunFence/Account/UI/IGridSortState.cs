namespace RunFence.Account.UI;

/// <summary>
/// Provides read-only access to the current grid sort state.
/// Implemented by <see cref="Forms.AccountsPanel"/> (via <see cref="RunFence.UI.Forms.DataPanel"/>)
/// and passed to handlers that need to observe sort state during refresh.
/// </summary>
public interface IGridSortState
{
    bool IsSortActive { get; }
    int SortColumnIndex { get; }
    bool SortDescending { get; }
}