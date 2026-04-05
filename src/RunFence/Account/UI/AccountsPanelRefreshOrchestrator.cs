namespace RunFence.Account.UI;

/// <summary>
/// Orchestrates grid refresh operations for the accounts panel, including initial async load,
/// process row state restoration, and scroll-position preservation.
/// Extracted to separate refresh coordination from the panel.
/// </summary>
public class AccountsPanelRefreshOrchestrator(AccountGridRefreshHandler refreshHandler)
{
    private DataGridView _grid = null!;
    private IGridSortState _sortState = null!;
    private IAccountGridCallbacks _callbacks = null!;
    private AccountProcessDisplayManager _processDisplayManager = null!;

    public void Initialize(DataGridView grid, IGridSortState sortState, IAccountGridCallbacks callbacks,
        AccountProcessDisplayManager processDisplayManager)
    {
        _grid = grid;
        _sortState = sortState;
        _callbacks = callbacks;
        _processDisplayManager = processDisplayManager;
        refreshHandler.Initialize(grid, sortState, callbacks);
    }

    public void RefreshGrid(Action? afterPopulate = null)
    {
        Dictionary<string, IReadOnlyList<ProcessInfo>>? prefetchedData = null;

        // Snapshot expanded SIDs before the async gap so concurrent ApplySidsWithProcesses calls
        // cannot clear _expandedSids. Process data is fetched in beforePopulate (still before
        // PopulateGrid clears the grid) to avoid a stale-or-missing list after the clear.
        var expandedSids = _processDisplayManager.GetExpandedSidsForRefresh();

        // Save scroll and process selection before the async gap — PopulateGrid clears the grid
        // and only restores AccountRow/ContainerRow selection, losing ProcessRow selection and
        // the exact scroll offset (selection restoration auto-scrolls to the selected row).
        int savedScrollPos = _grid.FirstDisplayedScrollingRowIndex;
        ProcessRow? savedProcessRow = _grid.SelectedRows.Count > 0
            ? _grid.SelectedRows[0].Tag as ProcessRow
            : null;

        Func<Task>? beforePopulate = expandedSids?.Count > 0
            ? async () => { prefetchedData = await _processDisplayManager.FetchRefreshDataAsync(expandedSids)!; }
            : null;

        Action combinedAfterPopulate = () =>
        {
            _processDisplayManager.ReapplyExpansion(prefetchedData);

            if (savedProcessRow != null)
                RestoreProcessRowSelection(savedProcessRow);

            if (savedScrollPos > 0 && _grid.Rows.Count > 0)
                try
                {
                    _grid.FirstDisplayedScrollingRowIndex = Math.Min(savedScrollPos, _grid.Rows.Count - 1);
                }
                catch
                {
                }

            afterPopulate?.Invoke();
        };

        if (beforePopulate != null)
            refreshHandler.RefreshGridWithPreFetch(beforePopulate, afterPopulate: combinedAfterPopulate);
        else
            refreshHandler.RefreshGrid(afterPopulate: combinedAfterPopulate);
    }

    public async Task InitialRefreshAsync()
    {
        _callbacks.UpdateStatus("Loading accounts...");
        await refreshHandler.InitialRefreshAsync();
        if (!_sortState.IsSortActive)
            _processDisplayManager.TriggerImmediateRefresh();
    }

    private void RestoreProcessRowSelection(ProcessRow target)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is ProcessRow pr && pr.Process.Pid == target.Process.Pid &&
                string.Equals(pr.OwnerSid, target.OwnerSid, StringComparison.OrdinalIgnoreCase))
            {
                _grid.ClearSelection();
                row.Selected = true;
                _grid.CurrentCell = row.Cells["Account"];
                return;
            }
        }
    }
}