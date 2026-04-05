using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.Account.UI;

/// <summary>
/// Handles account-specific context menu visibility and actions (kill processes, new app,
/// install packages, app navigation) for the accounts grid.
/// Container actions are handled by <see cref="ContainerContextMenuHandler"/>;
/// firewall by <see cref="AccountFirewallMenuHandler"/>;
/// process row menu by <see cref="AccountProcessMenuHandler"/>.
/// Coordinated by <see cref="AccountContextMenuOrchestrator"/>.
/// </summary>
public class AccountContextMenuHandler(
    IWindowsAccountService windowsAccountService,
    AccountLauncher launcher,
    ISessionProvider sessionProvider,
    IProcessTerminationService processTerminationService)
{
    private DataGridView _grid = null!;
    private AccountProcessDisplayManager? _processDisplayManager;

    public event Action<string>? AppNavigationRequested;
    public event Action<string>? NewAppRequested;
    public event Action<InstallablePackage>? InstallRequested;

    public void Initialize(DataGridView grid, AccountProcessDisplayManager? processDisplayManager)
    {
        _grid = grid;
        _processDisplayManager = processDisplayManager;
    }

    public void ShowAccountMenu(AccountRow accountRow, AccountContextMenuItems i, ContextMenuStrip contextMenu)
    {
        var db = sessionProvider.GetSession().Database;

        SetAccountItemsVisible(i, true);
        SetContainerItemsVisible(i, false);
        SetProcessItemsVisible(i, false);

        var isCurrentAccount = accountRow.Credential?.IsCurrentAccount == true;
        var isInteractiveUser = SidResolutionHelper.IsInteractiveUserSid(accountRow.Sid);
        var isUnavailable = accountRow.IsUnavailable;
        var canLaunch = !isUnavailable &&
                        (SidResolutionHelper.CanLaunchWithoutPassword(accountRow.Sid) || accountRow.HasStoredPassword);

        i.EditAccount.Enabled = !isUnavailable;

        var showAddAtTop = accountRow.Credential == null && !isCurrentAccount && !isUnavailable;
        i.AddCredential.Visible = showAddAtTop;
        i.AddCredentialSeparator.Visible = showAddAtTop;
        i.EditCredential.Visible = !showAddAtTop;
        i.EditCredential.Enabled = !isCurrentAccount && !isUnavailable;

        i.RemoveCredential.Enabled = accountRow.Credential != null && !isCurrentAccount && !isUnavailable;
        i.DeleteUser.Enabled = !isCurrentAccount && !isInteractiveUser && !isUnavailable;
        i.PinFolderBrowserToTray.Enabled = !string.IsNullOrEmpty(accountRow.Sid);
        i.PinFolderBrowserToTray.Checked = !string.IsNullOrEmpty(accountRow.Sid) &&
                                           db.GetAccount(accountRow.Sid)?.TrayFolderBrowser == true;
        i.PinDiscoveryToTray.Enabled = !string.IsNullOrEmpty(accountRow.Sid);
        i.PinDiscoveryToTray.Checked = !string.IsNullOrEmpty(accountRow.Sid) &&
                                       db.GetAccount(accountRow.Sid)?.TrayDiscovery == true;
        i.PinTerminalToTray.Enabled = !string.IsNullOrEmpty(accountRow.Sid);
        i.PinTerminalToTray.Checked = !string.IsNullOrEmpty(accountRow.Sid) &&
                                      db.GetAccount(accountRow.Sid)?.TrayTerminal == true;
        i.CopySid.Enabled = !string.IsNullOrEmpty(accountRow.Sid);
        i.CopyProfilePath.Enabled = !isUnavailable && !string.IsNullOrEmpty(accountRow.Sid);
        var profilePath = windowsAccountService.GetProfilePath(accountRow.Sid);
        i.OpenProfileFolder.Enabled = !isUnavailable && !string.IsNullOrEmpty(profilePath) && Directory.Exists(profilePath);
        i.CopyPassword.Enabled = accountRow.Credential != null && !isCurrentAccount && !isUnavailable && accountRow.HasStoredPassword;
        i.TypePassword.Enabled = accountRow.Credential != null && !isCurrentAccount && !isUnavailable && accountRow.HasStoredPassword;
        i.RotatePassword.Enabled = !isCurrentAccount && !isInteractiveUser && !isUnavailable;
        i.SetEmptyPassword.Enabled = !isUnavailable;

        i.EditSubmenu.Enabled = !isUnavailable;

        bool canInstall = !isUnavailable &&
                          (SidResolutionHelper.CanLaunchWithoutPassword(accountRow.Sid) || accountRow.HasStoredPassword);
        var useWindowsTerminal = canLaunch && launcher.ResolveTerminalExe(accountRow.Sid) != "cmd.exe";
        i.ManageSeparator.Visible = true;
        i.ManageSubmenu.Visible = true;
        i.ManageSubmenu.Enabled = !isUnavailable;
        i.AclManager.Enabled = !isUnavailable;
        i.FolderBrowser.Enabled = canLaunch;
        i.Cmd.Text = useWindowsTerminal ? "Terminal" : "CMD";
        i.Cmd.Enabled = canLaunch;
        i.EnvironmentVariables.Visible = true;
        i.EnvironmentVariables.Enabled = canLaunch;
        i.KillAllProcesses.Visible = true;
        var hasProcesses = !string.IsNullOrEmpty(accountRow.Sid) &&
                           (_processDisplayManager == null || _processDisplayManager.HasProcesses(accountRow.Sid));
        i.KillAllProcesses.Enabled = !isCurrentAccount &&
                                     !isInteractiveUser && !isUnavailable && hasProcesses;
        i.ManageLaunchSeparator.Visible = true;
        foreach (var (package, item) in i.InstallItems)
        {
            item.Visible = true;
            bool installed = launcher.IsPackageInstalled(package, accountRow.Sid);
            item.Text = installed ? $"{package.DisplayName} \u2713" : $"Install {package.DisplayName}";
            item.Enabled = canInstall && !installed;
        }

        i.FirewallAllowlist.Visible = true;
        i.FirewallAllowlist.Enabled = !isUnavailable;

        i.AppsSeparator.Visible = true;
        i.NewApp.Visible = true;
        i.NewApp.Enabled = (accountRow.Credential != null || isInteractiveUser) && !isUnavailable;
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

    public void InstallPackage(InstallablePackage package)
        => InstallRequested?.Invoke(package);

    private static void SetAccountItemsVisible(AccountContextMenuItems i, bool visible)
    {
        i.AddCredential.Visible = visible;
        i.AddCredentialSeparator.Visible = visible;
        i.EditSubmenu.Visible = visible;
        i.EditSeparator.Visible = visible;
        i.PinFolderBrowserToTray.Visible = visible;
        i.PinDiscoveryToTray.Visible = visible;
        i.PinTerminalToTray.Visible = visible;
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