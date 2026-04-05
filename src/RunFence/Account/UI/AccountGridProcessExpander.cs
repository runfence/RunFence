namespace RunFence.Account.UI;

/// <summary>
/// Manages the expand/collapse state of per-account process sub-rows in the accounts grid.
/// Handles insertion, removal, and in-place refresh of process rows without full grid rebuilds.
/// </summary>
public class AccountGridProcessExpander(IProcessListService processListService)
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

        var parentRow = FindParentRow(_grid, sid);
        if (parentRow == null)
            return;

        int scrollPos = _grid.FirstDisplayedScrollingRowIndex;
        if (!_expandedSids.Add(sid))
        {
            _expandedSids.Remove(sid);
            RemoveProcessRowsBelow(_grid, parentRow);
        }
        else
        {
            var processes = processListService.GetProcessesForSid(sid);
            InsertProcessRows(_grid, parentRow, processes, sid);
        }

        RestoreScrollPosition(_grid, scrollPos);
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
            var parentRow = FindParentRow(_grid, sid);
            if (parentRow == null)
                continue;
            if (processes.Count == 0)
            {
                _expandedSids.Remove(sid);
                continue;
            }

            _expandedSids.Add(sid);
            InsertProcessRows(_grid, parentRow, processes, sid);
        }
    }

    public List<string> GetExpandedSidSnapshot() => _expandedSids.ToList();

    public Dictionary<string, IReadOnlyList<ProcessInfo>> FetchRefreshData(List<string> sids)
    {
        var result = new Dictionary<string, IReadOnlyList<ProcessInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sid in sids)
            result[sid] = processListService.GetProcessesForSid(sid);
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
            var parentRow = FindParentRow(_grid, sid);
            if (parentRow == null)
                continue;
            ApplyFreshProcesses(_grid, parentRow, freshProcesses, sid);
        }

        RestoreScrollPosition(_grid, scrollPos);
        switch (selectedTag)
        {
            case AccountRow or ContainerRow:
                RestoreSelection(_grid, selectedTag);
                break;
            case ProcessRow selectedProcess:
                RestoreProcessSelection(_grid, selectedProcess);
                break;
        }
    }

    public HashSet<string> FetchSidsWithProcesses(IEnumerable<string> sids)
        => processListService.GetSidsWithProcesses(sids);

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
            var parentRow = FindParentRow(_grid, sid);
            if (parentRow != null)
                RemoveProcessRowsBelow(_grid, parentRow);
        }

        RestoreScrollPosition(_grid, scrollPos);

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

    private static void ApplyFreshProcesses(DataGridView grid, DataGridViewRow parentRow,
        IReadOnlyList<ProcessInfo> freshProcesses, string sid)
    {
        var freshPids = freshProcesses.Select(p => p.Pid).ToHashSet();

        var currentPids = new HashSet<int>();
        int checkIndex = parentRow.Index + 1;
        while (checkIndex < grid.Rows.Count && grid.Rows[checkIndex].Tag is ProcessRow pr)
        {
            currentPids.Add(pr.Process.Pid);
            checkIndex++;
        }

        if (freshPids.SetEquals(currentPids))
        {
            var freshByPid = freshProcesses.ToDictionary(p => p.Pid);
            int rowIndex = parentRow.Index + 1;
            int processIndex = 0;
            while (rowIndex < grid.Rows.Count && grid.Rows[rowIndex].Tag is ProcessRow processRow)
            {
                if (freshByPid.TryGetValue(processRow.Process.Pid, out var updated))
                    UpdateProcessRow(grid.Rows[rowIndex], updated, isLast: processIndex == currentPids.Count - 1);
                rowIndex++;
                processIndex++;
            }
        }
        else
        {
            RemoveProcessRowsBelow(grid, parentRow);
            InsertProcessRows(grid, parentRow, freshProcesses, sid);
        }
    }

    private static void InsertProcessRows(DataGridView grid, DataGridViewRow parentRow,
        IReadOnlyList<ProcessInfo> processes, string ownerSid)
    {
        var sorted = processes
            .OrderBy(p => p.ExecutablePath != null ? Path.GetFileName(p.ExecutablePath) : string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Pid)
            .ToList();

        int maxPidChars = sorted.Count > 0 ? sorted.Max(p => p.Pid.ToString().Length) : 1;
        int insertIndex = parentRow.Index + 1;
        for (int i = 0; i < sorted.Count; i++)
        {
            grid.Rows.Insert(insertIndex, 1);
            ConfigureProcessRow(grid.Rows[insertIndex], sorted[i], ownerSid, isLast: i == sorted.Count - 1, pidColumnChars: maxPidChars);
            insertIndex++;
        }
    }

    private static void RemoveProcessRowsBelow(DataGridView grid, DataGridViewRow parentRow)
    {
        int index = parentRow.Index + 1;
        while (index < grid.Rows.Count && grid.Rows[index].Tag is ProcessRow)
            grid.Rows.RemoveAt(index);
    }

    private static void ConfigureProcessRow(DataGridViewRow row, ProcessInfo process, string ownerSid, bool isLast, int pidColumnChars)
    {
        var displayLine = FormatDisplayLine(process);
        var foreColor = process.ExecutablePath == null ? Color.Gray : Color.Empty;
        row.Tag = new ProcessRow(process, ownerSid, isLast, displayLine, pidColumnChars);
        row.ReadOnly = true;
        row.DefaultCellStyle.BackColor = Color.FromArgb(0xF2, 0xF5, 0xFA);
        row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0xCC, 0xD9, 0xF0);
        row.DefaultCellStyle.SelectionForeColor = foreColor == Color.Empty ? SystemColors.ControlText : foreColor;
        if (foreColor != Color.Empty)
            row.DefaultCellStyle.ForeColor = foreColor;

        row.Cells["Account"].Value = "";
        row.Cells["Credential"].Value = AccountGridHelper.EmptyIcon;
        row.Cells["Import"] = new DataGridViewTextBoxCell { Value = "" };
        row.Cells["Logon"] = new DataGridViewTextBoxCell { Value = "" };
        row.Cells["colAllowInternet"] = new DataGridViewTextBoxCell { Value = "" };
        row.Cells["Apps"].Value = "";
        row.Cells["ProfilePath"].Value = "";
        row.Cells["SID"].Value = "";
    }

    private static void UpdateProcessRow(DataGridViewRow row, ProcessInfo process, bool isLast)
    {
        if (row.Tag is not ProcessRow pr)
            return;
        var displayLine = FormatDisplayLine(process);
        row.Tag = new ProcessRow(process, pr.OwnerSid, isLast, displayLine, pr.PidColumnChars);
        row.Cells["Account"].Value = "";
    }

    internal static string? StripExeFromCommandLine(string? cmdLine)
    {
        if (string.IsNullOrEmpty(cmdLine))
            return null;
        cmdLine = cmdLine.TrimStart();

        string remainder;
        if (cmdLine.StartsWith('"'))
        {
            int close = cmdLine.IndexOf('"', 1);
            remainder = close >= 0 ? cmdLine[(close + 1)..] : "";
        }
        else
        {
            int space = cmdLine.IndexOf(' ');
            remainder = space >= 0 ? cmdLine[space..] : "";
        }

        remainder = remainder.TrimStart();
        return remainder.Length > 0 ? remainder : null;
    }

    private static string FormatDisplayLine(ProcessInfo process)
    {
        var exeName = process.ExecutablePath != null
            ? Path.GetFileName(process.ExecutablePath)
            : $"[{process.Pid}]";
        var args = StripExeFromCommandLine(process.CommandLine);
        return args != null ? $"{process.Pid} {exeName} {args}" : $"{process.Pid} {exeName}";
    }

    private static void RestoreScrollPosition(DataGridView grid, int scrollPos)
    {
        if (scrollPos >= 0 && scrollPos < grid.Rows.Count)
            try
            {
                grid.FirstDisplayedScrollingRowIndex = scrollPos;
            }
            catch
            {
            }
    }

    private static void RestoreSelection(DataGridView grid, object selectedTag)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Tag == selectedTag && !row.Selected)
            {
                grid.ClearSelection();
                row.Selected = true;
                return;
            }
        }
    }

    private static void RestoreProcessSelection(DataGridView grid, ProcessRow target)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Tag is ProcessRow pr && pr.Process.Pid == target.Process.Pid &&
                string.Equals(pr.OwnerSid, target.OwnerSid, StringComparison.OrdinalIgnoreCase))
            {
                grid.ClearSelection();
                row.Selected = true;
                return;
            }
        }
    }

    private static DataGridViewRow? FindParentRow(DataGridView grid, string sid)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Tag is AccountRow ar && string.Equals(ar.Sid, sid, StringComparison.OrdinalIgnoreCase)
                || row.Tag is ContainerRow cr && string.Equals(cr.ContainerSid, sid, StringComparison.OrdinalIgnoreCase))
                return row;
        }

        return null;
    }

    public static string? GetSidFromRow(DataGridViewRow row) => row.Tag switch
    {
        AccountRow ar => ar.Sid,
        ContainerRow cr => cr.ContainerSid,
        _ => null
    };
}