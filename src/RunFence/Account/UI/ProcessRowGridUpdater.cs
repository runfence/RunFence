namespace RunFence.Account.UI;

/// <summary>
/// Handles process row grid manipulation for the accounts DataGridView.
/// </summary>
public class ProcessRowGridUpdater(ProcessCommandLineFormatter commandLineFormatter)
{
    public void ApplyFreshProcesses(
        DataGridView grid,
        DataGridViewRow parentRow,
        IReadOnlyList<ProcessInfo> freshProcesses,
        string sid)
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
            return;
        }

        RemoveProcessRowsBelow(grid, parentRow);
        InsertProcessRows(grid, parentRow, freshProcesses, sid);
    }

    public void InsertProcessRows(
        DataGridView grid,
        DataGridViewRow parentRow,
        IReadOnlyList<ProcessInfo> processes,
        string ownerSid)
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

    public void RemoveProcessRowsBelow(DataGridView grid, DataGridViewRow parentRow)
    {
        int index = parentRow.Index + 1;
        while (index < grid.Rows.Count && grid.Rows[index].Tag is ProcessRow)
        {
            grid.Rows.RemoveAt(index);
        }
    }

    public void RestoreScrollPosition(DataGridView grid, int scrollPos)
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

    public void RestoreSelection(DataGridView grid, object selectedTag)
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

    public void RestoreProcessSelection(DataGridView grid, ProcessRow target)
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

    public DataGridViewRow? FindParentRow(DataGridView grid, string sid)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Tag is AccountRow ar && string.Equals(ar.Sid, sid, StringComparison.OrdinalIgnoreCase)
                || row.Tag is ContainerRow cr && string.Equals(cr.ContainerSid, sid, StringComparison.OrdinalIgnoreCase))
                return row;
        }

        return null;
    }

    private void ConfigureProcessRow(
        DataGridViewRow row,
        ProcessInfo process,
        string ownerSid,
        bool isLast,
        int pidColumnChars)
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
        row.Cells["Import"] = CreateEmptyCell();
        row.Cells["Logon"] = CreateEmptyCell();
        row.Cells["colAllowInternet"] = CreateEmptyCell();
        row.Cells["Apps"].Value = "";
        row.Cells["ProfilePath"].Value = "";
        row.Cells["SID"].Value = "";
    }

    private void UpdateProcessRow(DataGridViewRow row, ProcessInfo process, bool isLast)
    {
        if (row.Tag is not ProcessRow pr)
            return;
        var displayLine = FormatDisplayLine(process);
        row.Tag = new ProcessRow(process, pr.OwnerSid, isLast, displayLine, pr.PidColumnChars);
        row.Cells["Account"].Value = "";
    }

    private string FormatDisplayLine(ProcessInfo process)
    {
        var exeName = process.ExecutablePath != null
            ? Path.GetFileName(process.ExecutablePath)
            : $"[{process.Pid}]";
        var args = commandLineFormatter.StripExecutable(process.CommandLine);
        return args != null ? $"{process.Pid} {exeName} {args}" : $"{process.Pid} {exeName}";
    }

    private static DataGridViewTextBoxCell CreateEmptyCell() => new() { Value = "" };
}
