using RunFence.Infrastructure;
using DiagProcess = System.Diagnostics.Process;

namespace RunFence.Account.UI;

/// <summary>
/// Handles context menu interactions for process rows in the accounts grid,
/// including visibility setup and click actions (copy path, close, kill, properties).
/// </summary>
public class AccountProcessMenuHandler(ShellHelper shellHelper)
{
    private DataGridView _grid = null!;
    private IAccountsPanelContext _context = null!;
    private AccountContextMenuItems _items = null!;

    public void Initialize(DataGridView grid, AccountContextMenuItems items, IAccountsPanelContext context)
    {
        _grid = grid;
        _items = items;
        _context = context;

        _items.CopyProcessPath.Click += OnCopyProcessPathClick;
        _items.CopyProcessPid.Click += OnCopyProcessPidClick;
        _items.CopyProcessArgs.Click += OnCopyProcessArgsClick;
        _items.CloseProcess.Click += OnCloseProcessClick;
        _items.KillProcess.Click += OnKillProcessClick;
        _items.ProcessProperties.Click += OnProcessPropertiesClick;
    }

    public void ShowProcessMenu(ProcessRow processRow)
    {
        var i = _items;
        SetProcessItemsVisible(true);

        bool hasPath = !string.IsNullOrEmpty(processRow.Process.ExecutablePath);
        var args = AccountGridProcessExpander.StripExeFromCommandLine(processRow.Process.CommandLine);
        i.CopyProcessPath.Enabled = hasPath;
        i.CopyProcessPid.Enabled = true;
        i.CopyProcessArgs.Enabled = args != null;
        i.CloseProcess.Enabled = true;
        i.KillProcess.Enabled = true;
        i.ProcessProperties.Enabled = hasPath;
    }

    private void SetProcessItemsVisible(bool visible)
    {
        _items.ProcessSeparator.Visible = visible;
        _items.CopyProcessPath.Visible = visible;
        _items.CopyProcessPid.Visible = visible;
        _items.CopyProcessArgs.Visible = visible;
        _items.CloseProcess.Visible = visible;
        _items.KillProcess.Visible = visible;
        _items.ProcessProperties.Visible = visible;
    }

    public void TriggerCloseProcess()
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not ProcessRow pr)
            return;

        var displayName = pr.Process.ExecutablePath != null
            ? Path.GetFileName(pr.Process.ExecutablePath)
            : pr.Process.Pid.ToString();
        var owner = _context.OwnerControl.FindForm();

        if (MessageBox.Show(owner,
                $"Close process \"{displayName}\" (PID {pr.Process.Pid})?",
                "Close Process", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        CloseProcess(pr);
    }

    private void OnCopyProcessPathClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not ProcessRow pr)
            return;
        if (string.IsNullOrEmpty(pr.Process.ExecutablePath))
            return;
        Clipboard.SetText(pr.Process.ExecutablePath);
    }

    private void OnCopyProcessPidClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not ProcessRow pr)
            return;
        Clipboard.SetText(pr.Process.Pid.ToString());
    }

    private void OnCopyProcessArgsClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not ProcessRow pr)
            return;
        var args = AccountGridProcessExpander.StripExeFromCommandLine(pr.Process.CommandLine);
        if (args != null)
            Clipboard.SetText(args);
    }

    private void OnCloseProcessClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not ProcessRow pr)
            return;
        CloseProcess(pr);
    }

    private void CloseProcess(ProcessRow pr)
    {
        try
        {
            var p = pr.Process.ProcessHandle;
            if (p != null)
            {
                p.CloseMainWindow();
            }
            else
            {
                using var byPid = DiagProcess.GetProcessById(pr.Process.Pid);
                byPid.CloseMainWindow();
            }
        }
        catch (ArgumentException)
        {
            /* already exited */
        }
        catch (Exception ex)
        {
            var owner = _context.OwnerControl.FindForm();
            MessageBox.Show(owner, $"Failed to close process: {ex.Message}", "Close Process",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        _context.TriggerProcessRefresh(1000);
    }

    private void OnKillProcessClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not ProcessRow pr)
            return;

        var displayName = pr.Process.ExecutablePath != null
            ? Path.GetFileName(pr.Process.ExecutablePath)
            : pr.Process.Pid.ToString();
        var owner = _context.OwnerControl.FindForm();

        if (MessageBox.Show(owner,
                $"Kill process \"{displayName}\" (PID {pr.Process.Pid})?",
                "Kill Process", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        try
        {
            var p = pr.Process.ProcessHandle;
            if (p != null)
            {
                p.Kill();
            }
            else
            {
                using var byPid = DiagProcess.GetProcessById(pr.Process.Pid);
                byPid.Kill();
            }
        }
        catch (ArgumentException)
        {
            /* already exited */
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"Failed to kill process: {ex.Message}", "Kill Process",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _context.TriggerProcessRefresh();
    }

    private void OnProcessPropertiesClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not ProcessRow pr)
            return;
        if (string.IsNullOrEmpty(pr.Process.ExecutablePath))
            return;
        shellHelper.ShowProperties(pr.Process.ExecutablePath, _context.OwnerControl.FindForm());
    }
}