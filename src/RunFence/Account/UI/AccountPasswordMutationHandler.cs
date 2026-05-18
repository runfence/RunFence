using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;

namespace RunFence.Account.UI;

public class AccountPasswordMutationHandler(
    ISessionProvider sessionProvider,
    ICredentialDecryptionService credentialDecryption,
    ILoggingService log,
    IAccountCredentialManager credentialManager,
    IAccountPasswordService accountPassword,
    IEvaluationLimitHelper evaluationLimitHelper,
    IModalCoordinator modalCoordinator,
    SidDisplayNameResolver displayNameResolver,
    IDatabaseProvider databaseProvider)
{
    private OperationGuard _guard = null!;
    private Control _ownerControl = null!;
    private Action<string> _setStatus = null!;
    private Control Parent => _ownerControl.FindForm() ?? _ownerControl;

    private enum NoStoredPasswordChoice
    {
        Cancel,
        ForceReset,
        EnterCurrentPassword
    }

    public void Initialize(OperationGuard guard, Control ownerControl, Action<string> setStatus)
    {
        _guard = guard;
        _ownerControl = ownerControl;
        _setStatus = setStatus;
    }

    public void RotatePassword(AccountRow accountRow, Action<Guid?> save)
    {
        var session = sessionProvider.GetSession();
        var pinKeySource = session.PinDerivedKey;
        var store = session.CredentialStore;
        if (accountRow.Credential == null &&
            !evaluationLimitHelper.CheckCredentialLimit(store.Credentials, Parent, extraMessage: "Right-click any credential in the list to remove it."))
            return;

        _guard.Begin(Parent);
        ProtectedString? oldPassword = null;
        ProtectedString? newPassword = null;
        var newPasswordChars = Array.Empty<char>();
        try
        {
            newPasswordChars = PasswordHelper.GenerateRandomPassword();
            newPassword = ProtectedString.FromChars(newPasswordChars);
            var decryptResult = pinKeySource.TransformSnapshot(key =>
            {
                var status = credentialDecryption.TryDecryptCredential(accountRow.Sid, store, key, out _, out var password);
                return (status, password);
            });
            oldPassword = decryptResult.password;
            var status = decryptResult.status;

            bool changed = status == CredentialLookupStatus.Success && oldPassword != null
                ? TryChangePassword(accountRow.Sid, accountRow.Username, oldPassword, newPassword, Parent)
                : TryChangePasswordNoStoredCredential(accountRow.Sid, accountRow.Username, newPassword, Parent);
            if (!changed)
                return;

            if (accountRow.Credential != null)
            {
                credentialManager.UpdateCredentialPassword(accountRow.Credential, newPassword, pinKeySource);
                save(accountRow.Credential.Id);
            }
            else
            {
                var (_, credId, _) = credentialManager.AddNewCredential(accountRow.Sid, newPassword, store, pinKeySource);
                save(credId);
            }

            _setStatus("Password rotated successfully.");
        }
        catch (Exception ex)
        {
            log.Error($"Failed to rotate password for {accountRow.Username}", ex);
            MessageBox.Show($"Failed to rotate password: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Array.Clear(newPasswordChars, 0, newPasswordChars.Length);
            newPassword?.Dispose();
            oldPassword?.Dispose();
            _guard.End(Parent);
        }
    }

    public void SetEmptyPassword(AccountRow accountRow, Action<Guid?> save)
    {
        var session = sessionProvider.GetSession();
        var pinKeySource = session.PinDerivedKey;
        var store = session.CredentialStore;
        var database = databaseProvider.GetDatabase();
        var displayName = accountRow.Credential != null
            ? displayNameResolver.GetDisplayName(accountRow.Credential, database.SidNames)
            : displayNameResolver.GetDisplayName(accountRow.Sid, accountRow.Username, database.SidNames);
        if (MessageBox.Show(
                $"Set empty password for “{displayName}”?\n\nThe account will be usable without a password.",
                "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _guard.Begin(Parent);
        ProtectedString? oldPassword = null;
        try
        {
            var decryptResult = pinKeySource.TransformSnapshot(key =>
            {
                var status = credentialDecryption.TryDecryptCredential(accountRow.Sid, store, key, out _, out var password);
                return (status, password);
            });
            oldPassword = decryptResult.password;
            var status = decryptResult.status;

            using var emptyPassword = ProtectedString.CreateEmpty();
            bool changed = status == CredentialLookupStatus.Success && oldPassword != null
                ? TryChangePassword(accountRow.Sid, accountRow.Username, oldPassword, emptyPassword, Parent)
                : TryChangePasswordNoStoredCredential(accountRow.Sid, accountRow.Username, emptyPassword, Parent);
            if (!changed)
                return;

            Guid? credIdToSelect = null;
            if (accountRow.Credential != null)
            {
                if (accountRow.Credential.IsCurrentAccount)
                    credIdToSelect = accountRow.Credential.Id;
                else
                    credentialManager.RemoveCredential(accountRow.Credential.Id, store);
            }

            save(credIdToSelect);
            _setStatus("Password set to empty.");
        }
        catch (Exception ex)
        {
            log.Error($"Failed to set empty password for {accountRow.Username}", ex);
            MessageBox.Show($"Failed to set empty password: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            oldPassword?.Dispose();
            _guard.End(Parent);
        }
    }

    private bool TryChangePassword(string sid, string username, ProtectedString oldPassword, ProtectedString newPassword, Control parent)
    {
        var result = accountPassword.ChangeAccountPassword(sid, oldPassword, newPassword);
        if (result.Status == AccountPasswordStatus.Succeeded)
            return true;
        if (result.Status == AccountPasswordStatus.InvalidPassword)
            return TryChangePasswordNoStoredCredential(sid, username, newPassword, parent);
        throw new InvalidOperationException(result.Error ?? "Failed to change password.");
    }

    private bool TryChangePasswordNoStoredCredential(string sid, string username, ProtectedString newPassword, Control parent)
    {
        using (var emptyPwd = ProtectedString.CreateEmpty())
        {
            var emptyResult = accountPassword.ChangeAccountPassword(sid, emptyPwd, newPassword);
            if (emptyResult.Status == AccountPasswordStatus.Succeeded)
                return true;
            if (emptyResult.Status != AccountPasswordStatus.InvalidPassword)
                throw new InvalidOperationException(emptyResult.Error ?? "Failed to change password.");
        }

        var choice = PromptNoStoredPasswordChoice(username, parent);
        switch (choice)
        {
            case NoStoredPasswordChoice.ForceReset:
                var resetResult = accountPassword.AdminResetAccountPassword(sid, newPassword);
                if (resetResult.Status != AccountPasswordStatus.Succeeded)
                    throw new InvalidOperationException(resetResult.Error ?? "Failed to reset password.");
                return true;
            case NoStoredPasswordChoice.EnterCurrentPassword:
            {
                var enteredPwd = PromptCurrentPassword(username);
                if (enteredPwd == null)
                    return false;

                using (enteredPwd)
                {
                    var enteredResult = accountPassword.ChangeAccountPassword(sid, enteredPwd, newPassword);
                    if (enteredResult.Status == AccountPasswordStatus.Succeeded)
                        return true;
                    if (enteredResult.Status == AccountPasswordStatus.InvalidPassword)
                    {
                        MessageBox.Show("Incorrect password.", "Wrong Password", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
                    throw new InvalidOperationException(enteredResult.Error ?? "Failed to change password.");
                }
            }
            default:
                return false;
        }
    }

    private static NoStoredPasswordChoice PromptNoStoredPasswordChoice(string username, Control parent)
    {
        var forceResetBtn = new TaskDialogButton("Force Reset");
        var enterPwdBtn = new TaskDialogButton("Enter Current Password");
        var cancelBtn = new TaskDialogButton("Cancel");

        var page = new TaskDialogPage
        {
            Caption = "Password Required",
            Heading = $"Cannot change password for “{username}” automatically.",
            Text = "Enter the current Windows password, or force-reset it.\n\nWarning: force reset will lose EFS-encrypted files and Windows Credential Manager entries.",
            Icon = TaskDialogIcon.Warning,
            Buttons = { forceResetBtn, enterPwdBtn, cancelBtn },
            DefaultButton = enterPwdBtn,
            AllowCancel = true
        };

        var result = TaskDialog.ShowDialog(parent, page);
        if (result == forceResetBtn)
            return NoStoredPasswordChoice.ForceReset;
        if (result == enterPwdBtn)
            return NoStoredPasswordChoice.EnterCurrentPassword;
        return NoStoredPasswordChoice.Cancel;
    }

    private ProtectedString? PromptCurrentPassword(string username)
    {
        DialogResult result = DialogResult.None;
        ProtectedString? password = null;
        modalCoordinator.RunOnSecureDesktop(() =>
        {
            using var dlg = new PasswordInputDialog(username);
            result = dlg.ShowDialog();
            password = dlg.Password;
        });

        return result == DialogResult.OK ? password : null;
    }
}
