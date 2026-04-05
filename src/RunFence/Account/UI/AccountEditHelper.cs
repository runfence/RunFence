using System.Security;
using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.PrefTrans;
using RunFence.UI.Forms;

namespace RunFence.Account.UI;

/// <summary>
/// Handles the distinct phases of the EditAccount flow: password change, desktop settings import,
/// and firewall rules application. Extracted from <see cref="AccountCredentialOperations.EditAccount"/>.
/// </summary>
public class AccountEditHelper
{
    private readonly IAccountCredentialManager _credentialManager;
    private readonly IAccountPasswordService _accountPassword;
    private readonly ISettingsTransferService _settingsTransferService;
    private readonly IFirewallService _firewallService;
    private readonly ISessionProvider _sessionProvider;
    private readonly OperationGuard _operationGuard;

    public AccountEditHelper(
        IAccountCredentialManager credentialManager,
        IAccountPasswordService accountPassword,
        ISettingsTransferService settingsTransferService,
        ISessionProvider sessionProvider,
        OperationGuard operationGuard,
        IFirewallService firewallService)
    {
        _credentialManager = credentialManager;
        _accountPassword = accountPassword;
        _settingsTransferService = settingsTransferService;
        _sessionProvider = sessionProvider;
        _operationGuard = operationGuard;
        _firewallService = firewallService;
    }

    /// <summary>
    /// Tries to apply the password change. If the stored credential provides the old password,
    /// uses it directly; otherwise shows the PasswordChangeMethodDialog on the secure desktop.
    /// Returns true if a password was successfully applied to the OS account.
    /// </summary>
    public bool ApplyPasswordChange(AccountRow accountRow, EditAccountDialog dlg, bool isCurrentAccount)
    {
        if (dlg.NewPasswordText == null)
            return false;

        var session = _sessionProvider.GetSession();

        // Try with stored password first
        if (accountRow is { Credential: not null, HasStoredPassword: true })
        {
            var status = _credentialManager.DecryptCredential(accountRow.Sid, session.CredentialStore, session.PinDerivedKey, out var oldPwd);
            if (status == CredentialLookupStatus.Success && oldPwd != null)
            {
                try
                {
                    _accountPassword.ChangeAccountPassword(accountRow.Sid, oldPwd, dlg.NewPasswordText);
                    return true;
                }
                catch
                {
                    // stored password failed — fall through to method dialog
                }
                finally
                {
                    oldPwd.Dispose();
                }
            }
        }

        // Stored password unavailable or failed — show method dialog
        bool forceResetRequested = false;
        SecureString? enteredPassword = null;
        DialogResult methodResult = DialogResult.None;

        DataPanel.RunOnSecureDesktop(() =>
        {
            using var methodDlg = new PasswordChangeMethodDialog(isCurrentAccount);
            methodResult = methodDlg.ShowDialog();
            forceResetRequested = methodDlg.ForceResetRequested;
            enteredPassword = methodDlg.EnteredPassword;
        });

        if (methodResult != DialogResult.OK)
            return false;

        if (forceResetRequested)
        {
            try
            {
                _accountPassword.AdminResetAccountPassword(accountRow.Sid, dlg.NewPasswordText);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to reset password: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        if (enteredPassword != null)
        {
            try
            {
                _accountPassword.ChangeAccountPassword(accountRow.Sid, enteredPassword, dlg.NewPasswordText);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to change password: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                enteredPassword.Dispose();
            }
        }

        return false;
    }

    /// <summary>
    /// Performs the desktop settings import if requested. For the current account or interactive user,
    /// runs de-elevated; for other accounts, acquires the password from the new or stored credential,
    /// then runs the import on a background thread and records any granted paths.
    /// </summary>
    public async Task ImportDesktopSettingsAsync(AccountRow accountRow, EditAccountDialog dlg,
        string effectiveUsername, bool passwordApplied, bool isCurrentAccount, Control ownerControl)
    {
        if (dlg.SettingsImportPath == null)
            return;

        var session = _sessionProvider.GetSession();

        var tokenSource = isCurrentAccount ? LaunchTokenSource.CurrentProcess
            : SidResolutionHelper.IsInteractiveUserSid(accountRow.Sid) ? LaunchTokenSource.InteractiveUser
            : LaunchTokenSource.Credentials;

        SecureString? importPwd = null;
        if (tokenSource == LaunchTokenSource.Credentials)
        {
            if (dlg.NewPasswordText != null && passwordApplied)
            {
                importPwd = new SecureString();
                foreach (char c in dlg.NewPasswordText)
                    importPwd.AppendChar(c);
                importPwd.MakeReadOnly();
            }
            else if (accountRow is { Credential: not null, HasStoredPassword: true })
            {
                var status = _credentialManager.DecryptCredential(accountRow.Sid, session.CredentialStore, session.PinDerivedKey, out importPwd);
                if (status != CredentialLookupStatus.Success)
                    importPwd = null;
            }

            if (importPwd == null)
            {
                dlg.Errors.Add("Settings import skipped: no password available.");
                return;
            }
        }

        _operationGuard.Begin(ownerControl);
        try
        {
            var creds = new LaunchCredentials(importPwd, ".", effectiveUsername, tokenSource);
            var (error, hadGrants) = await SettingsImportHelper.ImportAsync(
                dlg.SettingsImportPath, creds, accountRow.Sid,
                _settingsTransferService);
            if (hadGrants)
                _credentialManager.SaveConfig(session.Database, session.PinDerivedKey, session.CredentialStore.ArgonSalt);
            if (error != null)
                dlg.Errors.Add($"Settings import: {error}");
        }
        catch (Exception ex)
        {
            dlg.Errors.Add($"Settings import: {ex.Message}");
        }
        finally
        {
            importPwd?.Dispose();
            _operationGuard.End(ownerControl);
        }
    }

    /// <summary>
    /// Applies firewall OS rules after the database has been persisted, so OS state never diverges
    /// from persisted state.
    /// </summary>
    public void ApplyFirewallRules(AccountRow accountRow, FirewallAccountSettings? newFirewallSettings)
    {
        if (newFirewallSettings == null)
            return;

        var session = _sessionProvider.GetSession();
        var username = session.Database.SidNames.GetValueOrDefault(accountRow.Sid) ?? accountRow.Username;
        _firewallService.ApplyFirewallRules(accountRow.Sid, username, newFirewallSettings);
    }
}