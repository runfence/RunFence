namespace RunFence.Account.UI;

/// <summary>
/// Implements type-ahead keyboard navigation for the accounts grid.
/// When on an account or container row, navigates within all account and container rows.
/// When on a process row, navigates within the process rows of the same parent account.
/// Search goes forward from the current row and wraps to the start of the scope.
/// Typing the same character repeatedly cycles through matching rows.
/// The prefix resets after 1 second of inactivity.
/// </summary>
public class AccountGridTypeAheadHandler
{
    private DataGridView _grid = null!;
    private string _prefix = "";
    private DateTime _lastKeyTime = DateTime.MinValue;
    private const int PrefixResetMs = 1000;

    public void Initialize(DataGridView grid)
    {
        _grid = grid;
    }

    public void HandleKeyPress(KeyPressEventArgs e)
    {
        if (!char.IsLetterOrDigit(e.KeyChar))
            return;
        if (_grid.IsCurrentCellInEditMode)
            return;

        var currentRow = _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0] : null;
        var scope = GetSearchScope(currentRow);
        if (scope.Count == 0)
            return;

        var now = DateTime.UtcNow;
        bool expired = (now - _lastKeyTime).TotalMilliseconds > PrefixResetMs;

        if (expired)
        {
            _prefix = e.KeyChar.ToString();
        }
        else if (_prefix.Length == 1 && char.ToUpperInvariant(e.KeyChar) == char.ToUpperInvariant(_prefix[0]))
        {
            // Same single char: keep prefix unchanged to cycle to next match
        }
        else
        {
            _prefix += e.KeyChar;
        }

        _lastKeyTime = now;
        e.Handled = true;

        int currentIndexInScope = currentRow != null ? scope.IndexOf(currentRow) : -1;
        int startIndex = (currentIndexInScope + 1) % scope.Count;
        var found = FindMatchingRow(scope, startIndex, _prefix);

        if (found != null)
            SelectRow(found);
    }

    private List<DataGridViewRow> GetSearchScope(DataGridViewRow? currentRow)
    {
        if (currentRow?.Tag is ProcessRow)
            return GetProcessScope(currentRow);
        return GetAccountScope();
    }

    private List<DataGridViewRow> GetProcessScope(DataGridViewRow processRow)
    {
        // Walk back to the start of this contiguous block of ProcessRows
        int startIdx = processRow.Index;
        while (startIdx > 0 && _grid.Rows[startIdx - 1].Tag is ProcessRow)
            startIdx--;

        var scope = new List<DataGridViewRow>();
        for (int i = startIdx; i < _grid.Rows.Count && _grid.Rows[i].Tag is ProcessRow; i++)
            scope.Add(_grid.Rows[i]);
        return scope;
    }

    private List<DataGridViewRow> GetAccountScope()
    {
        var scope = new List<DataGridViewRow>();
        foreach (DataGridViewRow row in _grid.Rows)
            if (row.Tag is AccountRow or ContainerRow)
                scope.Add(row);
        return scope;
    }

    private static DataGridViewRow? FindMatchingRow(List<DataGridViewRow> scope, int startIndex, string prefix)
    {
        int count = scope.Count;
        for (int i = 0; i < count; i++)
        {
            var row = scope[(startIndex + i) % count];
            var key = GetRowSearchKey(row);
            if (key != null && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return row;
        }

        return null;
    }

    private static string? GetRowSearchKey(DataGridViewRow row) => row.Tag switch
    {
        ProcessRow pr => pr.Process.ExecutablePath != null
            ? Path.GetFileName(pr.Process.ExecutablePath)
            : pr.Process.Pid.ToString(),
        _ => row.Cells["Account"].Value as string
    };

    private void SelectRow(DataGridViewRow row)
    {
        _grid.ClearSelection();
        row.Selected = true;
        _grid.CurrentCell = row.Cells["Account"];
        EnsureRowVisible(row);
    }

    private void EnsureRowVisible(DataGridViewRow row)
    {
        if (_grid.DisplayedRowCount(false) == 0)
            return;
        int first = _grid.FirstDisplayedScrollingRowIndex;
        int displayed = _grid.DisplayedRowCount(false);
        if (row.Index < first || row.Index >= first + displayed)
            try
            {
                _grid.FirstDisplayedScrollingRowIndex = row.Index;
            }
            catch
            {
            }
    }
}