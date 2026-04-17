using System.Security;
using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall.UI;
using RunFence.Infrastructure;
using RunFence.PrefTrans;

namespace RunFence.Account.UI;

/// <summary>
/// Handles the distinct phases of the EditAccount flow: password change, desktop settings import,
/// and firewall rules application. Extracted from <see cref="AccountCredentialOperations.EditAccount"/>.
/// </summary>
public class AccountEditHelper(
    IModalCoordinator modalCoordinator,
    SessionPersistenceHelper persistenceHelper,
    IAccountCredentialManager credentialManager,
    IAccountPasswordService accountPassword,
    ISettingsTransferService settingsTransferService,
    ISessionProvider sessionProvider,
    OperationGuard operationGuard,
    FirewallApplyHelper firewallApplyHelper)
{
    /// <summary>
    /// Tries to apply the password change. If the stored credential provides the old password,
    /// uses it directly; otherwise shows the PasswordChangeMethodDialog on the secure desktop.
    /// Returns true if a password was successfully applied to the OS account.
    /// </summary>
    public bool ApplyPasswordChange(AccountRow accountRow, EditAccountDialog dlg, bool isCurrentAccount)
    {
        if (dlg.NewPasswordText == null)
            return false;

        var session = sessionProvider.GetSession();

        // Try with stored password first
        if (accountRow is { Credential: not null, HasStoredPassword: true })
        {
            var status = credentialManager.DecryptCredential(accountRow.Sid, session.CredentialStore, session.PinDerivedKey, out var oldPwd);
            if (status == CredentialLookupStatus.Success && oldPwd != null)
            {
                try
                {
                    accountPassword.ChangeAccountPassword(accountRow.Sid, oldPwd, dlg.NewPasswordText);
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

        modalCoordinator.RunOnSecureDesktop(() =>
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
                accountPassword.AdminResetAccountPassword(accountRow.Sid, dlg.NewPasswordText);
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
                accountPassword.ChangeAccountPassword(accountRow.Sid, enteredPassword, dlg.NewPasswordText);
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
    /// Performs the desktop settings import if requested and records any granted paths.
    /// </summary>
    public async Task ImportDesktopSettingsAsync(AccountRow accountRow, EditAccountDialog dlg, Control ownerControl)
    {
        if (dlg.SettingsImportPath == null)
            return;

        var session = sessionProvider.GetSession();

        operationGuard.Begin(ownerControl);
        try
        {
            var (error, hadGrants) = await SettingsImportHelper.ImportAsync(
                dlg.SettingsImportPath, accountRow.Sid,
                settingsTransferService);
            if (hadGrants)
                persistenceHelper.SaveConfig(session.Database, session.PinDerivedKey, session.CredentialStore.ArgonSalt);
            if (error != null)
                dlg.Errors.Add($"Settings import: {error}");
        }
        catch (Exception ex)
        {
            dlg.Errors.Add($"Settings import: {ex.Message}");
        }
        finally
        {
            operationGuard.End(ownerControl);
        }
    }

    /// <summary>
    /// Applies firewall OS rules after the database has been persisted, so OS state never diverges
    /// from persisted state.
    /// </summary>
    public void ApplyFirewallRules(
        AccountRow accountRow,
        FirewallAccountSettings? previousFirewallSettings,
        FirewallAccountSettings? newFirewallSettings)
    {
        if (newFirewallSettings == null)
            return;

        var session = sessionProvider.GetSession();
        var username = session.Database.SidNames.GetValueOrDefault(accountRow.Sid) ?? accountRow.Username;
        firewallApplyHelper.ApplyWithRollback(
            owner: null,
            sid: accountRow.Sid,
            username: username,
            previous: previousFirewallSettings,
            final: newFirewallSettings,
            database: session.Database,
            saveAction: () => persistenceHelper.SaveConfig(session.Database, session.PinDerivedKey, session.CredentialStore.ArgonSalt));
    }
}
