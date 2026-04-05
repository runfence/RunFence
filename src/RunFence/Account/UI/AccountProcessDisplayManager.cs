namespace RunFence.Account.UI;

/// <summary>
/// Manages the process-display lifecycle for the accounts grid: expander, painter, and optional refresh timer.
/// Composes four tightly coupled process-related deps that are always used together,
/// providing a single point of control for "display running processes" state.
/// </summary>
public class AccountProcessDisplayManager(
    AccountGridProcessExpander processExpander,
    AccountProcessRowPainter processRowPainter,
    AccountProcessTimerManager? timerManager) : IDisposable
{
    private DataGridView _grid = null!;

    public void Initialize(DataGridView grid, IGridSortState sortState)
    {
        _grid = grid;
        bool hasProcessService = timerManager != null;
        processExpander.Initialize(grid);
        processRowPainter.Initialize(grid, sortState, hasProcessService);
        timerManager?.Initialize(grid, processExpander, () => sortState.IsSortActive);
    }

    public void Start(Func<bool> isVisibleAndParentVisible)
    {
        _grid.CellPainting += (_, e) => processRowPainter.Paint(e);
        _grid.RowPostPaint += (_, e) => processRowPainter.PostPaint(e);

        timerManager?.Start(isVisibleAndParentVisible);
    }

    public void NotifyParentResized(bool isMinimized)
        => timerManager?.HandleParentFormResize(isMinimized);

    public void NotifyVisibilityChanged(bool isVisible)
        => timerManager?.HandleVisibilityChanged(isVisible);

    public void TriggerImmediateRefresh()
        => timerManager?.TriggerImmediateRefresh();

    public void TriggerDelayedRefresh(int delayMs)
        => timerManager?.TriggerDelayedRefresh(delayMs);

    public void ToggleExpand(string sid)
    {
        if (timerManager == null)
            return;
        processExpander.Toggle(sid);
    }

    public string? GetProcessRowTooltip(ProcessRow processRow)
        => processRowPainter.GetProcessRowTooltip(processRow);

    // --- Process expander query methods (for grid interaction and context menu) ---

    public bool HasProcesses(string sid) => processExpander.HasProcesses(sid);
    public bool IsExpanded(string sid) => processExpander.IsExpanded(sid);

    // --- Refresh coordination methods (for AccountsPanelRefreshOrchestrator) ---

    /// <summary>Reapplies expansion using data pre-fetched before the grid was cleared.</summary>
    public void ReapplyExpansion(Dictionary<string, IReadOnlyList<ProcessInfo>>? prefetchedData)
    {
        if (prefetchedData != null)
            processExpander.ReapplyExpansion(prefetchedData);
    }

    /// <summary>Returns the set of currently expanded SIDs, or null when sort is active or no rows are expanded.</summary>
    public IReadOnlyList<string>? GetExpandedSidsForRefresh()
        => timerManager?.GetExpandedSidsForRefresh();

    /// <summary>Pre-fetches process data for the given SIDs before the grid is cleared.</summary>
    public Task<Dictionary<string, IReadOnlyList<ProcessInfo>>>? FetchRefreshDataAsync(IReadOnlyList<string> sids)
        => timerManager?.FetchRefreshDataAsync(sids);

    public void Dispose() => timerManager?.Dispose();
}