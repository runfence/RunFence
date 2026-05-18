namespace RunFence.Account.UI;

/// <summary>
/// Manages the expand/collapse state of per-account process sub-rows in the accounts grid.
/// Handles insertion, removal, and in-place refresh of process rows without full grid rebuilds.
/// </summary>
public class AccountGridProcessExpander(
    IProcessListService processListService,
    ProcessRowGridUpdater processRowGridUpdater)
{
    private DataGridView _grid = null!;
    private readonly HashSet<string> _expandedSids = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _sidsWithProcesses = new(StringComparer.OrdinalIgnoreCase);

    public void Initialize(DataGridView grid)
    {
        _grid = grid;
    }

    public bool IsExpanded(string sid) => _expandedSids.Contains(sid);
    public bool HasProcesses(string sid) => _sidsWithProcesses.Contains(sid);
    public bool HasExpandedRows => _expandedSids.Count > 0;

    public void Toggle(string? sid)
    {
        if (string.IsNullOrEmpty(sid))
            return;

        var parentRow = processRowGridUpdater.FindParentRow(_grid, sid);
        if (parentRow == null)
            return;

        int scrollPos = _grid.FirstDisplayedScrollingRowIndex;
        if (!_expandedSids.Add(sid))
        {
            _expandedSids.Remove(sid);
            processRowGridUpdater.RemoveProcessRowsBelow(_grid, parentRow);
        }
        else
        {
            var processes = processListService.GetProcessesForSid(sid);
            processRowGridUpdater.InsertProcessRows(_grid, parentRow, processes, sid);
        }

        processRowGridUpdater.RestoreScrollPosition(_grid, scrollPos);
    }

    /// <summary>
    /// Re-inserts process rows using data pre-fetched before the grid was cleared.
    /// Also restores <see cref="_expandedSids"/> state that may have been cleared by a concurrent
    /// <see cref="ApplySidsWithProcesses"/> call during the async gap.
    /// </summary>
    public void ReapplyExpansion(Dictionary<string, IReadOnlyList<ProcessInfo>> prefetchedData)
    {
        foreach (var (sid, processes) in prefetchedData)
        {
            var parentRow = processRowGridUpdater.FindParentRow(_grid, sid);
            if (parentRow == null)
                continue;
            if (processes.Count == 0)
            {
                _expandedSids.Remove(sid);
                continue;
            }

            _expandedSids.Add(sid);
            processRowGridUpdater.InsertProcessRows(_grid, parentRow, processes, sid);
        }
    }

    public List<string> GetExpandedSidSnapshot() => _expandedSids.ToList();

    public Dictionary<string, IReadOnlyList<ProcessInfo>> FetchRefreshData(List<string> sids, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IReadOnlyList<ProcessInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sid in sids)
            result[sid] = processListService.GetProcessesForSid(sid, cancellationToken);
        return result;
    }

    public void ApplyRefreshData(Dictionary<string, IReadOnlyList<ProcessInfo>> data)
    {
        object? selectedTag = _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Tag : null;
        int scrollPos = _grid.FirstDisplayedScrollingRowIndex;

        foreach (var sid in _expandedSids.ToList())
        {
            if (!data.TryGetValue(sid, out var freshProcesses))
                continue;
            var parentRow = processRowGridUpdater.FindParentRow(_grid, sid);
            if (parentRow == null)
                continue;
            processRowGridUpdater.ApplyFreshProcesses(_grid, parentRow, freshProcesses, sid);
        }

        processRowGridUpdater.RestoreScrollPosition(_grid, scrollPos);
        switch (selectedTag)
        {
            case AccountRow or ContainerRow:
                processRowGridUpdater.RestoreSelection(_grid, selectedTag);
                break;
            case ProcessRow selectedProcess:
                processRowGridUpdater.RestoreProcessSelection(_grid, selectedProcess);
                break;
        }
    }

    public HashSet<string> FetchSidsWithProcesses(IEnumerable<string> sids, CancellationToken cancellationToken)
        => processListService.GetSidsWithProcesses(sids, cancellationToken);

    public void ApplySidsWithProcesses(HashSet<string> withProcesses)
    {
        var added = withProcesses.Except(_sidsWithProcesses, StringComparer.OrdinalIgnoreCase).ToList();
        var removed = _sidsWithProcesses.Except(withProcesses, StringComparer.OrdinalIgnoreCase).ToList();

        _sidsWithProcesses = withProcesses;

        int scrollPos = _grid.FirstDisplayedScrollingRowIndex;
        foreach (var sid in removed)
        {
            if (!_expandedSids.Contains(sid))
                continue;
            _expandedSids.Remove(sid);
            var parentRow = processRowGridUpdater.FindParentRow(_grid, sid);
            if (parentRow != null)
                processRowGridUpdater.RemoveProcessRowsBelow(_grid, parentRow);
        }

        processRowGridUpdater.RestoreScrollPosition(_grid, scrollPos);

        foreach (DataGridViewRow row in _grid.Rows)
        {
            string? sid = GetSidFromRow(row);
            if (sid == null)
                continue;
            if (added.Any(s => string.Equals(s, sid, StringComparison.OrdinalIgnoreCase)) ||
                removed.Any(s => string.Equals(s, sid, StringComparison.OrdinalIgnoreCase)))
            {
                _grid.InvalidateRow(row.Index);
            }
        }
    }

    public static string? GetSidFromRow(DataGridViewRow row) => row.Tag switch
    {
        AccountRow ar => ar.Sid,
        ContainerRow cr => cr.ContainerSid,
        _ => null
    };
}
