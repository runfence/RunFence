using RunFence.Infrastructure;

namespace RunFence.Account.UI;

/// <summary>
/// Handles account-specific context menu visibility and actions (kill processes, new app,
/// install packages, app navigation) for the accounts grid.
/// Container actions are handled by <see cref="ContainerContextMenuHandler"/>;
/// firewall by <see cref="AccountFirewallMenuHandler"/>;
/// process row menu by <see cref="AccountProcessMenuHandler"/>.
/// Coordinated by <see cref="AccountContextMenuOrchestrator"/>.
/// Menu item state configuration is delegated to <see cref="AccountMenuStateConfigurator"/>.
/// </summary>
public class AccountContextMenuHandler(
    AccountMenuStateConfigurator menuStateConfigurator,
    ISessionProvider sessionProvider,
    IProcessTerminationService processTerminationService)
{
    private DataGridView _grid = null!;
    private AccountProcessDisplayManager? _processDisplayManager;

    public event Action<string>? AppNavigationRequested;
    public event Action<string>? NewAppRequested;

    public void Initialize(DataGridView grid, AccountProcessDisplayManager? processDisplayManager)
    {
        _grid = grid;
        _processDisplayManager = processDisplayManager;
    }

    public void ShowAccountMenu(AccountRow accountRow, AccountContextMenuItems i, ContextMenuStrip contextMenu)
    {
        SetAccountItemsVisible(i, true);
        SetContainerItemsVisible(i, false);
        SetProcessItemsVisible(i, false);

        menuStateConfigurator.ConfigureMenuState(accountRow, i, _processDisplayManager);

        var db = sessionProvider.GetSession().Database;
        int insertIndex = contextMenu.Items.IndexOf(i.NewApp);

        var linkedApps = db.Apps
            .Where(a => string.Equals(a.AccountSid, accountRow.Sid, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        foreach (var app in linkedApps)
        {
            var appItem = new ToolStripMenuItem(app.Name);
            var capturedId = app.Id;
            appItem.Click += (_, _) => AppNavigationRequested?.Invoke(capturedId);
            i.DynamicAppItems.Add(appItem);
            contextMenu.Items.Insert(insertIndex, appItem);
            insertIndex++;
        }
    }

    public void ShowContainerMenu(ContainerRow containerRow, AccountContextMenuItems i)
    {
        SetAccountItemsVisible(i, false);
        SetProcessItemsVisible(i, false);

        var hasContainerSid = !string.IsNullOrEmpty(containerRow.ContainerSid);

        i.ManageSeparator.Visible = true;
        i.ManageSubmenu.Visible = true;
        i.ManageSubmenu.Enabled = true;
        i.AclManager.Enabled = hasContainerSid;
        i.FolderBrowser.Enabled = true;
        i.Cmd.Text = "CMD";
        i.Cmd.Enabled = true;
        i.FirewallAllowlist.Visible = false;
        i.EnvironmentVariables.Visible = false;
        i.KillAllProcesses.Visible = false;
        i.ManageLaunchSeparator.Visible = false;
        foreach (var (_, item) in i.InstallItems)
            item.Visible = false;

        SetContainerItemsVisible(i, true);
        i.ContainerSeparator.Visible = false;
        i.CreateContainer.Visible = false;
        i.CopySid.Visible = true;
        i.CopySid.Enabled = hasContainerSid;
        i.AppsSeparator.Visible = false;
        i.NewApp.Visible = false;
    }

    public void KillAllProcesses(AccountRow accountRow)
    {
        if (string.IsNullOrEmpty(accountRow.Sid))
            return;

        var owner = _grid.FindForm();
        if (MessageBox.Show(owner,
                $"Kill all processes running under \"{accountRow.Username}\"?\n\nThis will forcefully terminate all processes owned by this account.",
                "Kill All Processes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        int killed, failed;
        Cursor.Current = Cursors.WaitCursor;
        try
        {
            (killed, failed) = processTerminationService.KillProcesses(accountRow.Sid);
        }
        catch (Exception ex)
        {
            Cursor.Current = Cursors.Default;
            MessageBox.Show(owner, $"Failed to enumerate processes: {ex.Message}", "Kill All Processes",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Cursor.Current = Cursors.Default;

        if (killed == 0 && failed == 0)
            MessageBox.Show(owner, "No processes found for this account.", "Kill All Processes",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        else if (failed == 0)
            MessageBox.Show(owner, $"Killed {killed} process{(killed == 1 ? "" : "es")}.", "Kill All Processes",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        else
            MessageBox.Show(owner,
                $"Killed {killed} process{(killed == 1 ? "" : "es")}. {failed} could not be terminated.",
                "Kill All Processes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    public void RequestNewApp(string sid)
    {
        if (!string.IsNullOrEmpty(sid))
            NewAppRequested?.Invoke(sid);
    }

    public static void SetAccountItemsVisible(AccountContextMenuItems i, bool visible)
    {
        i.AddCredential.Visible = visible;
        i.AddCredentialSeparator.Visible = visible;
        i.EditSubmenu.Visible = visible;
        i.EditSeparator.Visible = visible;
        i.PinFolderBrowserToTray.Visible = visible;
        i.PinDiscoveryToTray.Visible = visible;
        i.PinTerminalToTray.Visible = visible;
        i.ShowInRunAs.Visible = false;
        i.ManageAssociations.Visible = visible;
        i.ReceiveInjectedInput.Visible = visible;
        i.CopySid.Visible = visible;
        i.CopyProfilePath.Visible = visible;
        i.OpenProfileFolder.Visible = visible;
        i.CopyPassword.Visible = visible;
        i.TypePassword.Visible = visible;
        i.Sep4.Visible = visible;
        i.Sep5.Visible = visible;
        i.ManageSeparator.Visible = visible;
        i.ManageSubmenu.Visible = visible;
    }

    public static void SetContainerItemsVisible(AccountContextMenuItems i, bool visible)
    {
        i.ContainerSeparator.Visible = visible;
        i.CreateContainer.Visible = visible;
        i.EditContainer.Visible = visible;
        i.DeleteContainer.Visible = visible;
        i.CopyContainerProfilePath.Visible = visible;
        i.OpenContainerProfileFolder.Visible = visible;
    }

    private static void SetProcessItemsVisible(AccountContextMenuItems i, bool visible)
    {
        i.ProcessSeparator.Visible = visible;
        i.CopyProcessPath.Visible = visible;
        i.CopyProcessPid.Visible = visible;
        i.CopyProcessArgs.Visible = visible;
        i.CloseProcess.Visible = visible;
        i.KillProcess.Visible = visible;
        i.ProcessProperties.Visible = visible;
    }
}
