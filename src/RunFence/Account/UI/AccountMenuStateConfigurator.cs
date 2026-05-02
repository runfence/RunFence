using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Account.UI;

/// <summary>
/// Configures the enabled/visible/checked state of all account-row context menu items.
/// Handles both normal accounts and the SYSTEM account special-casing.
/// Menu creation and event wiring remain in <see cref="AccountContextMenuHandler"/>.
/// </summary>
public class AccountMenuStateConfigurator(
    IWindowsAccountService windowsAccountService,
    ISessionProvider sessionProvider,
    AccountToolResolver toolResolver,
    PackageInstallService packageInstallService)
{
    public void ConfigureMenuState(
        AccountRow accountRow,
        AccountContextMenuItems i,
        AccountProcessDisplayManager? processDisplayManager)
    {
        var db = sessionProvider.GetSession().Database;
        var isSystem = SidResolutionHelper.IsSystemSid(accountRow.Sid);

        var isCurrentAccount = accountRow.Credential?.IsCurrentAccount == true
            || string.Equals(accountRow.Sid, SidResolutionHelper.GetCurrentUserSid(), StringComparison.OrdinalIgnoreCase);
        var isInteractiveUser = SidResolutionHelper.IsInteractiveUserSid(accountRow.Sid);
        var isUnavailable = accountRow.IsUnavailable;
        var canLaunch = accountRow.CanLaunch;

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
        i.ManageAssociations.Enabled = !string.IsNullOrEmpty(accountRow.Sid);
        i.ManageAssociations.Checked = !string.IsNullOrEmpty(accountRow.Sid) &&
                                       (db.GetAccount(accountRow.Sid)?.ManageAssociations ?? true);
        i.ReceiveInjectedInput.Enabled = !string.IsNullOrEmpty(accountRow.Sid);
        i.ReceiveInjectedInput.Checked = !string.IsNullOrEmpty(accountRow.Sid) &&
                                         (db.GetAccount(accountRow.Sid)?.ReceiveInjectedInput == true);
        i.CopySid.Enabled = !string.IsNullOrEmpty(accountRow.Sid);
        i.CopyProfilePath.Enabled = !isUnavailable && !string.IsNullOrEmpty(accountRow.Sid);
        var profilePath = windowsAccountService.GetProfilePath(accountRow.Sid);
        i.OpenProfileFolder.Enabled = !isUnavailable && !string.IsNullOrEmpty(profilePath) && Directory.Exists(profilePath);
        i.CopyPassword.Enabled = accountRow.Credential != null && !isCurrentAccount && !isUnavailable && accountRow.HasStoredPassword;
        i.TypePassword.Enabled = accountRow.Credential != null && !isCurrentAccount && !isUnavailable && accountRow.HasStoredPassword;
        i.RotatePassword.Enabled = !isCurrentAccount && !isInteractiveUser && !isUnavailable;
        // Allowing empty password for the current admin account is by design —
        // it lets the admin set up logon access without a credential entry.
        i.SetEmptyPassword.Enabled = !isUnavailable;

        i.EditSubmenu.Enabled = !isUnavailable;

        var useWindowsTerminal = canLaunch && !toolResolver.ResolveTerminalExe(accountRow.Sid).Equals("cmd.exe", StringComparison.OrdinalIgnoreCase);
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
                           (processDisplayManager == null || processDisplayManager.HasProcesses(accountRow.Sid));
        i.KillAllProcesses.Enabled = !isCurrentAccount &&
                                     !isInteractiveUser && !isUnavailable && hasProcesses;
        i.ManageLaunchSeparator.Visible = true;
        foreach (var (package, item) in i.InstallItems)
        {
            item.Visible = true;
            bool installed = packageInstallService.IsPackageInstalled(package, accountRow.Sid);
            item.Text = installed ? $"{package.DisplayName} \u2713" : $"Install {package.DisplayName}";
            item.Enabled = canLaunch && !installed;
        }

        i.FirewallAllowlist.Visible = true;
        i.FirewallAllowlist.Enabled = !isUnavailable;

        i.AppsSeparator.Visible = true;
        i.NewApp.Visible = true;
        i.NewApp.Enabled = (accountRow.Credential != null || isInteractiveUser) && !isUnavailable;

        if (isSystem)
        {
            // Credential/edit/account ops: visible but disabled
            i.EditAccount.Enabled = false;
            i.EditSubmenu.Enabled = false;
            // AddCredential shortcut: keep visible per "do not hide" rule but disable
            i.AddCredential.Enabled = false;
            // General logic hides EditCredential when showAddAtTop is true; restore as visible-but-disabled
            i.EditCredential.Visible = true;
            i.EditCredential.Enabled = false;
            i.RemoveCredential.Enabled = false;
            i.DeleteUser.Enabled = false;
            // Tray pins: Folder Browser + Terminal enabled, Discovery disabled
            i.PinDiscoveryToTray.Enabled = false;
            // Other account ops
            i.ManageAssociations.Enabled = false;
            i.ReceiveInjectedInput.Enabled = false;
            i.CopyProfilePath.Enabled = false;
            i.OpenProfileFolder.Enabled = false;
            i.CopyPassword.Enabled = false;
            i.TypePassword.Enabled = false;
            i.RotatePassword.Enabled = false;
            i.SetEmptyPassword.Enabled = false;
            i.FirewallAllowlist.Enabled = false;
            i.KillAllProcesses.Enabled = false;
            i.AclManager.Enabled = false;
            foreach (var (_, item) in i.InstallItems)
                item.Enabled = false;
            // NewApp: allow — user can create App Entries for SYSTEM
            i.NewApp.Enabled = true;
            // Show in RunAs checkbox (SYSTEM-only)
            i.ShowInRunAs.Visible = true;
            i.ShowInRunAs.Checked = db.ShowSystemInRunAs;
        }
    }
}
