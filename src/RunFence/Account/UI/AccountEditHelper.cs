using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall.UI;
using RunFence.Infrastructure;
using RunFence.PrefTrans;

namespace RunFence.Account.UI;

public class AccountEditHelper(
    IModalCoordinator modalCoordinator,
    SessionPersistenceHelper persistenceHelper,
    ICredentialDecryptionService credentialDecryption,
    IAccountPasswordService accountPassword,
    ISettingsTransferService settingsTransferService,
    ISessionProvider sessionProvider,
    OperationGuard operationGuard,
    FirewallApplyHelper firewallApplyHelper)
{
    public bool ApplyPasswordChange(AccountRow accountRow, IAccountEditResult editResult, bool isCurrentAccount)
    {
        if (editResult.NewPassword == null)
            return false;

        var session = sessionProvider.GetSession();
        var pinKeySource = session.PinDerivedKey;
        if (accountRow is { Credential: not null, HasStoredPassword: true })
        {
            var decryptResult = pinKeySource.TransformSnapshot(key =>
            {
                var status = credentialDecryption.TryDecryptCredential(
                    accountRow.Sid, session.CredentialStore, key, out _, out var decryptedPassword);
                return (status, decryptedPassword);
            });
            var oldPwd = decryptResult.decryptedPassword;
            var status = decryptResult.status;
            if (status == CredentialLookupStatus.Success && oldPwd != null)
            {
                var result = accountPassword.ChangeAccountPassword(accountRow.Sid, oldPwd, editResult.NewPassword);
                oldPwd.Dispose();
                if (result.Status == AccountPasswordStatus.Succeeded)
                    return true;
            }
        }

        bool forceResetRequested = false;
        ProtectedString? enteredPassword = null;
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
            var resetResult = accountPassword.AdminResetAccountPassword(accountRow.Sid, editResult.NewPassword);
            if (resetResult.Status == AccountPasswordStatus.Succeeded)
                return true;
            MessageBox.Show($"Failed to reset password: {resetResult.Error}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        if (enteredPassword == null)
            return false;

        var changeResult = accountPassword.ChangeAccountPassword(accountRow.Sid, enteredPassword, editResult.NewPassword);
        enteredPassword.Dispose();
        if (changeResult.Status == AccountPasswordStatus.Succeeded)
            return true;
        MessageBox.Show($"Failed to change password: {changeResult.Error}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return false;
    }

    public async Task ImportDesktopSettingsAsync(AccountRow accountRow, IAccountEditResult editResult, Control ownerControl)
    {
        if (editResult.SettingsImportPath == null)
            return;

        var session = sessionProvider.GetSession();
        operationGuard.Begin(ownerControl);
        try
        {
            var importResult = await SettingsImportHelper.ImportAsync(editResult.SettingsImportPath, accountRow.Sid, settingsTransferService);
            if (importResult.Status != SettingsImportStatus.Succeeded)
                editResult.Errors.Add($"Settings import: {string.Join("; ", importResult.Errors)}");
        }
        catch (Exception ex)
        {
            editResult.Errors.Add($"Settings import: {ex.Message}");
        }
        finally
        {
            operationGuard.End(ownerControl);
        }
    }

    public void ApplyFirewallRules(AccountRow accountRow, FirewallAccountSettings? previousFirewallSettings, FirewallAccountSettings? newFirewallSettings)
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
            saveAction: () => persistenceHelper.SaveConfig(
                session.Database,
                session.PinDerivedKey,
                session.CredentialStore.ArgonSalt));
    }
}
