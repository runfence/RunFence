namespace RunFence.Account.UI;

/// <summary>
/// Interprets grid input events for the accounts panel and returns domain-level actions.
/// Contains no business operations — callers act on the returned action values.
/// Covers keyboard, cell-click, double-click, right-click, and toggle-expand interpretation.
/// </summary>
public class AccountsPanelGridInteraction
{
    // --- Cell click ---

    public GridClickAction HandleCellContentClick(DataGridViewRow row, string colName)
    {
        if (row.Tag is AccountRow accountRow)
        {
            switch (colName)
            {
                case "Logon" when !row.Cells[colName].ReadOnly:
                    return GridClickAction.ToggleLogon(accountRow);
                case "colAllowInternet" when !row.Cells[colName].ReadOnly:
                    return GridClickAction.ToggleInternet(accountRow);
            }
        }
        else if (row.Tag is ContainerRow containerRow && colName == "colAllowInternet" && !row.Cells[colName].ReadOnly)
        {
            return GridClickAction.ToggleContainerInternet(containerRow);
        }

        return GridClickAction.None;
    }

    // --- Double click ---

    public GridDoubleClickAction HandleDoubleClick(DataGridViewRow row)
    {
        return row.Tag switch
        {
            AccountRow { IsUnavailable: true } => GridDoubleClickAction.None,
            ContainerRow or AccountRow => GridDoubleClickAction.OpenAclManager,
            _ => GridDoubleClickAction.None
        };
    }

    // --- Mouse click (for expand toggle + right-click row select) ---

    public GridMouseClickResult HandleMouseClick(
        DataGridViewCellMouseEventArgs e,
        DataGridView grid,
        AccountProcessDisplayManager processDisplayManager,
        bool isSortActive)
    {
        if (e is { Button: MouseButtons.Left, RowIndex: >= 0, ColumnIndex: >= 0 } &&
            grid.Columns[e.ColumnIndex].Name == "Account" &&
            !isSortActive && e.X < 20)
        {
            var row = grid.Rows[e.RowIndex];
            string? sid = AccountGridProcessExpander.GetSidFromRow(row);
            if (!string.IsNullOrEmpty(sid) && processDisplayManager.HasProcesses(sid))
                return GridMouseClickResult.ExpandToggle(sid);
        }

        if (e is { Button: MouseButtons.Right, RowIndex: >= 0 } && grid.Rows[e.RowIndex].Tag is AccountGroupHeader)
            return GridMouseClickResult.RightClickHeader;

        if (e is { Button: MouseButtons.Right, RowIndex: >= 0 })
            return GridMouseClickResult.RightClickRow;

        return GridMouseClickResult.None;
    }

    // --- Key handling ---

    public KeyAction HandleKeyDown(Keys keyCode, DataGridViewRow? selectedRow, AccountProcessDisplayManager processDisplayManager, bool isSortActive)
    {
        switch (keyCode)
        {
            case Keys.Delete:
                if (selectedRow?.Tag is ProcessRow)
                    return KeyAction.CloseSelectedProcess;
                return KeyAction.RemoveCredential;
            case Keys.F2:
                return KeyAction.BeginInlineRename;
            case Keys.Enter:
                return KeyAction.EditAccount;
            case Keys.Apps:
            case Keys.F10 when (Control.ModifierKeys & Keys.Shift) != 0:
                return KeyAction.ShowContextMenu;
            case Keys.Right when !isSortActive:
            {
                if (selectedRow == null)
                    break;
                string? sid = AccountGridProcessExpander.GetSidFromRow(selectedRow);
                if (sid != null && processDisplayManager.HasProcesses(sid) && !processDisplayManager.IsExpanded(sid))
                    return KeyAction.ExpandRow(sid);
                break;
            }
            case Keys.Left when !isSortActive:
            {
                if (selectedRow == null)
                    break;
                if (selectedRow.Tag is ProcessRow processRow)
                    return KeyAction.NavigateToOwner(processRow.OwnerSid);
                string? sid = AccountGridProcessExpander.GetSidFromRow(selectedRow);
                if (sid != null && processDisplayManager.IsExpanded(sid))
                    return KeyAction.CollapseRow(sid);
                break;
            }
        }

        return KeyAction.None;
    }

    public bool HandleCmdKey(Keys keyData, DataGridViewRow? selectedRow)
    {
        if (keyData == Keys.Space && selectedRow != null)
        {
            if (!selectedRow.Cells["Import"].ReadOnly)
                selectedRow.Cells["Import"].Value = selectedRow.Cells["Import"].Value is not true;
            return true;
        }

        return false;
    }
}

// --- Action result types ---

public abstract record GridClickAction
{
    public static readonly GridClickAction None = new NoneAction();
    public static GridClickAction ToggleLogon(AccountRow row) => new ToggleLogonAction(row);
    public static GridClickAction ToggleInternet(AccountRow row) => new ToggleInternetAction(row);
    public static GridClickAction ToggleContainerInternet(ContainerRow row) => new ToggleContainerInternetAction(row);

    public record NoneAction : GridClickAction;

    public record ToggleLogonAction(AccountRow AccountRow) : GridClickAction;

    public record ToggleInternetAction(AccountRow AccountRow) : GridClickAction;

    public record ToggleContainerInternetAction(ContainerRow ContainerRow) : GridClickAction;
}

public enum GridDoubleClickAction
{
    None,
    OpenAclManager
}

public abstract record GridMouseClickResult
{
    public static readonly GridMouseClickResult None = new NoneResult();
    public static GridMouseClickResult ExpandToggle(string sid) => new ExpandToggleResult(sid);
    public static readonly GridMouseClickResult RightClickHeader = new RightClickHeaderResult();
    public static readonly GridMouseClickResult RightClickRow = new RightClickRowResult();

    public record NoneResult : GridMouseClickResult;

    public record ExpandToggleResult(string Sid) : GridMouseClickResult;

    public record RightClickHeaderResult : GridMouseClickResult;

    public record RightClickRowResult : GridMouseClickResult;
}

public abstract record KeyAction
{
    public static readonly KeyAction None = new NoneAction();
    public static readonly KeyAction CloseSelectedProcess = new CloseSelectedProcessAction();
    public static readonly KeyAction RemoveCredential = new RemoveCredentialAction();
    public static readonly KeyAction BeginInlineRename = new BeginInlineRenameAction();
    public static readonly KeyAction EditAccount = new EditAccountAction();
    public static readonly KeyAction ShowContextMenu = new ShowContextMenuAction();
    public static KeyAction ExpandRow(string sid) => new ExpandRowAction(sid);
    public static KeyAction CollapseRow(string sid) => new CollapseRowAction(sid);
    public static KeyAction NavigateToOwner(string ownerSid) => new NavigateToOwnerAction(ownerSid);

    public record NoneAction : KeyAction;

    public record CloseSelectedProcessAction : KeyAction;

    public record RemoveCredentialAction : KeyAction;

    public record BeginInlineRenameAction : KeyAction;

    public record EditAccountAction : KeyAction;

    public record ShowContextMenuAction : KeyAction;

    public record ExpandRowAction(string Sid) : KeyAction;

    public record CollapseRowAction(string Sid) : KeyAction;

    public record NavigateToOwnerAction(string OwnerSid) : KeyAction;
}