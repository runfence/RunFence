using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Account.UI;

/// <summary>
/// Handles simple account panel actions: rename account, copy SID, copy profile path,
/// open profile folder, and copy random password.
/// </summary>
public class AccountPanelActions(IWindowsAccountService windowsAccountService, ILocalUserProvider localUserProvider, ILoggingService log, ISidNameCacheService sidNameCache, ShellHelper shellHelper, ISystemDialogLauncher systemDialogLauncher)
{
    private DataGridView _grid = null!;
    private IAccountsPanelContext _context = null!;

    public void Initialize(DataGridView grid, IAccountsPanelContext context)
    {
        _grid = grid;
        _context = context;
    }

    public void CopySid(string sid)
    {
        Clipboard.SetText(sid);
    }

    public void CopyProfilePath(string sid)
    {
        var path = windowsAccountService.GetProfilePath(sid);
        if (!string.IsNullOrEmpty(path))
            Clipboard.SetText(path);
        else
            MessageBox.Show("No profile path found for this account.", "Profile Path", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public void OpenProfileFolder(string sid)
    {
        var path = windowsAccountService.GetProfilePath(sid);
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            MessageBox.Show("No profile folder found for this account.", "Profile Folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            shellHelper.OpenInExplorer(path);
        }
        catch
        {
            /* best effort */
        }
    }

    public void BeginInlineRename()
    {
        if (_grid.SelectedRows.Count == 0)
            return;
        var row = _grid.SelectedRows[0];
        if (row.Tag is not AccountRow accountRow)
            return;
        if (accountRow.IsUnavailable)
            return;
        // Current account and interactive user renaming is intentionally allowed
        _grid.CurrentCell = row.Cells["Account"];
        _context.RenameInProgress = true;
        _grid.BeginEdit(true);
    }

    public void CommitRename(AccountRow accountRow, DataGridViewRow row, string newName, string? originalCellValue)
    {
        _context.OperationGuard.Begin(_context.OwnerControl);
        try
        {
            windowsAccountService.RenameAccount(accountRow.Sid, accountRow.Username, newName);
            // Use the known new name directly — TryResolveName uses the LSA cache
            // which may still return the old name immediately after a rename.
            sidNameCache.UpdateName(accountRow.Sid, $"{Environment.MachineName}\\{newName}");
            _context.SaveAndRefresh();
        }
        catch (Exception ex)
        {
            log.Error($"Failed to rename account {accountRow.Username} to {newName}", ex);
            row.Cells["Account"].Value = originalCellValue;
            MessageBox.Show($"Failed to rename account: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _context.OperationGuard.End(_context.OwnerControl);
        }
    }

    public void OpenUserAccountsDialog()
    {
        try
        {
            systemDialogLauncher.OpenUserAccountsDialog();
        }
        catch (Exception ex)
        {
            log.Error("Failed to open User Accounts dialog", ex);
            MessageBox.Show($"Failed to open dialog: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void InvalidateLocalUserCache()
        => localUserProvider.InvalidateCache();

    public void CopyRandomPassword()
    {
        var passwordChars = PasswordHelper.GenerateRandomPassword();
        try
        {
            var dataObject = new DataObject(DataFormats.UnicodeText, new string(passwordChars));
            dataObject.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
            // No auto-clear of the clipboard: the user explicitly requested password generation and
            // needs time to use it more than once. Clipboard lifetime is their responsibility.
            Clipboard.SetDataObject(dataObject, copy: true);
            _context.UpdateStatus("Random password copied to clipboard.");
        }
        finally
        {
            Array.Clear(passwordChars, 0, passwordChars.Length);
        }
    }
}