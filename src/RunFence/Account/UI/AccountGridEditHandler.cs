namespace RunFence.Account.UI;

/// <summary>
/// Handles grid cell editing events for the accounts panel: begin/end edit, dirty-state
/// changes, value changes, validation, editing-control setup, no-logon toggle, and
/// allow-internet toggle.
/// </summary>
public class AccountGridEditHandler(IAccountToggleService accountToggle, AccountPanelActions panelActions)
{
    private DataGridView _grid = null!;
    private IAccountsPanelContext _context = null!;
    private string? _originalAccountCellValue;
    public bool RenameInProgress { get; set; }

    public void Initialize(DataGridView grid, IAccountsPanelContext context)
    {
        _grid = grid;
        _context = context;
    }

    public void HandleCellBeginEdit(DataGridViewCellCancelEventArgs e)
    {
        if (e.RowIndex < 0)
            return;
        if (_grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ReadOnly || _grid.Columns[e.ColumnIndex].ReadOnly)
        {
            e.Cancel = true;
            return;
        }

        if (_grid.Columns[e.ColumnIndex].Name == "Account")
        {
            if (!RenameInProgress)
            {
                e.Cancel = true;
                return;
            }

            _originalAccountCellValue = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value as string;
        }
    }

    public void HandleCellEndEdit(DataGridViewCellEventArgs e)
    {
        RenameInProgress = false;
        if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Account")
            return;
        var row = _grid.Rows[e.RowIndex];
        if (row.Tag is not AccountRow accountRow)
            return;
        var newName = (row.Cells[e.ColumnIndex].Value as string)?.Trim();
        if (string.IsNullOrWhiteSpace(newName) ||
            string.Equals(newName, _originalAccountCellValue, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(newName, accountRow.Username, StringComparison.OrdinalIgnoreCase))
        {
            row.Cells[e.ColumnIndex].Value = _originalAccountCellValue;
            return;
        }

        panelActions.CommitRename(accountRow, row, newName, _originalAccountCellValue);
    }

    public void HandleDirtyStateChanged()
    {
        if (_grid.IsCurrentCellDirty && _grid.CurrentCell?.OwningColumn?.Name != "Account")
            _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    public void HandleCellValueChanged(DataGridViewCellEventArgs e)
    {
        if (_context.IsRefreshing || e.RowIndex < 0)
            return;
        if (_grid.Columns[e.ColumnIndex].Name == "Import")
            _context.UpdateButtonState();
    }

    public void HandleCellValidating(DataGridViewCellValidatingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Account")
            return;
        if (_grid.Rows[e.RowIndex].Tag is ProcessRow)
            return;
        if (string.IsNullOrWhiteSpace(e.FormattedValue as string))
            e.Cancel = true;
    }

    public void HandleEditingControlShowing(DataGridViewEditingControlShowingEventArgs e)
    {
        if (_grid.CurrentCell?.OwningColumn?.Name != "Account")
            return;
        if (e.Control is not TextBox tb)
            return;
        tb.MaxLength = 20;
        if (_grid.CurrentRow?.Tag is AccountRow accountRow)
        {
            tb.Text = accountRow.Username;
            tb.SelectAll();
        }
    }

    public void HandleLogonToggle(DataGridViewRow row, AccountRow accountRow)
    {
        _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        var cell = (DataGridViewCheckBoxCell)row.Cells["Logon"];
        // Cell value type depends on per-cell ThreeState: bool when false, CheckState when true.
        // Checked = logon allowed, so blocked = NOT checked.
        var setBlocked = !(cell.Value is CheckState cs ? cs == CheckState.Checked : cell.Value is true);

        var result = accountToggle.SetLogonBlocked(accountRow.Sid, accountRow.Username, setBlocked);
        if (!result.Success)
        {
            var title = result.IsLicenseLimit ? "License Limit" : "Error";
            var icon = result.IsLicenseLimit ? MessageBoxIcon.Information : MessageBoxIcon.Error;
            MessageBox.Show(result.ErrorMessage!, title, MessageBoxButtons.OK, icon);
            _context.RefreshGrid();
            return;
        }

        _context.SaveAndRefresh();
    }

    public void HandleAllowInternetToggle(DataGridViewRow row, AccountRow accountRow)
    {
        _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        var cell = (DataGridViewCheckBoxCell)row.Cells["colAllowInternet"];
        var allowInternet = cell.Value is true;

        var error = accountToggle.SetAllowInternet(accountRow.Sid, accountRow.Username, allowInternet);
        if (error != null)
            MessageBox.Show(error, "RunFence", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        _context.SaveAndRefresh();
    }
}