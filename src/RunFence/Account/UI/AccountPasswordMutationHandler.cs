using System.ComponentModel;
using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Licensing;
using RunFence.Persistence;

namespace RunFence.Account.UI;

/// <summary>
/// Handles password mutation operations: rotate (generate new password) and set empty password.
/// Does not require PIN verification — uses the stored credential or prompts for the current password.
/// </summary>
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

    // Resolves the parent form for dialogs at call time (form may not be attached at Initialize time).
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
        var store = session.CredentialStore;
        if (accountRow.Credential == null
            && !evaluationLimitHelper.CheckCredentialLimit(store.Credentials, Parent,
                extraMessage: "Right-click any credential in the list to remove it."))
            return;

        _guard.Begin(Parent);
        ProtectedString? oldPassword = null;
        ProtectedString? newPassword = null;
        var newPasswordChars = Array.Empty<char>();
        try
        {
            newPasswordChars = PasswordHelper.GenerateRandomPassword();
            newPassword = ProtectedString.FromChars(newPasswordChars);
            using var rotateScope = session.PinDerivedKey.Unprotect();
            var status = credentialDecryption.TryDecryptCredential(accountRow.Sid, store, rotateScope.Data, out _, out oldPassword);

            bool changed;
            if (status == CredentialLookupStatus.Success && oldPassword != null)
                changed = TryChangePassword(accountRow.Sid, accountRow.Username, oldPassword, newPassword, Parent);
            else
                changed = TryChangePasswordNoStoredCredential(accountRow.Sid, accountRow.Username, newPassword, Parent);

            if (!changed)
                return;

            if (accountRow.Credential != null)
            {
                credentialManager.UpdateCredentialPassword(accountRow.Credential, newPassword, session.PinDerivedKey);
                save(accountRow.Credential.Id);
            }
            else
            {
                var (_, credId, _) = credentialManager.AddNewCredential(accountRow.Sid, newPassword, store, session.PinDerivedKey);
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
        var store = session.CredentialStore;
        var database = databaseProvider.GetDatabase();
        var displayName = accountRow.Credential != null
            ? displayNameResolver.GetDisplayName(accountRow.Credential, database.SidNames)
            : displayNameResolver.GetDisplayName(accountRow.Sid, accountRow.Username, database.SidNames);
        if (MessageBox.Show(
                $"Set empty password for \u201C{displayName}\u201D?\n\nThe account will be usable without a password.",
                "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _guard.Begin(Parent);
        ProtectedString? oldPassword = null;
        try
        {
            using var emptyPwdScope = session.PinDerivedKey.Unprotect();
            var status = credentialDecryption.TryDecryptCredential(accountRow.Sid, store, emptyPwdScope.Data, out _, out oldPassword);

            using var emptyPassword = ProtectedString.CreateEmpty();
            bool changed;
            if (status == CredentialLookupStatus.Success && oldPassword != null)
                changed = TryChangePassword(accountRow.Sid, accountRow.Username, oldPassword, emptyPassword, Parent);
            else
                changed = TryChangePasswordNoStoredCredential(accountRow.Sid, accountRow.Username, emptyPassword, Parent);

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

    // Returns true if the password was changed, false if cancelled.
    // Other exceptions propagate to the caller's catch block.
    private bool TryChangePassword(string sid, string username, ProtectedString oldPassword, ProtectedString newPassword, Control parent)
    {
        try
        {
            accountPassword.ChangeAccountPassword(sid, oldPassword, newPassword);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 86 or ProcessLaunchNative.Win32ErrorLogonFailure)
        {
            return TryChangePasswordNoStoredCredential(sid, username, newPassword, parent);
        }
    }

    // Called when no password is stored for the account. First tries an empty password; if that
    // fails, prompts the user to enter the current password or force-reset.
    // Returns true if the password was changed successfully.
    private bool TryChangePasswordNoStoredCredential(string sid, string username, ProtectedString newPassword, Control parent)
    {
        using (var emptyPwd = ProtectedString.CreateEmpty())
        {
            try
            {
                accountPassword.ChangeAccountPassword(sid, emptyPwd, newPassword);
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode is 86 or ProcessLaunchNative.Win32ErrorLogonFailure)
            {
                // Empty password didn't work — fall through to dialog
            }
        }

        var choice = PromptNoStoredPasswordChoice(username, parent);

        switch (choice)
        {
            case NoStoredPasswordChoice.ForceReset:
                accountPassword.AdminResetAccountPassword(sid, newPassword);
                return true;
            case NoStoredPasswordChoice.EnterCurrentPassword:
            {
                var enteredPwd = PromptCurrentPassword(username);
                if (enteredPwd == null)
                    return false;

                using (enteredPwd)
                {
                    try
                    {
                        accountPassword.ChangeAccountPassword(sid, enteredPwd, newPassword);
                        return true;
                    }
                    catch (Win32Exception ex) when (ex.NativeErrorCode is 86 or ProcessLaunchNative.Win32ErrorLogonFailure)
                    {
                        MessageBox.Show("Incorrect password.", "Wrong Password", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
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
            Heading = $"Cannot change password for \u201C{username}\u201D automatically.",
            Text = "Enter the current Windows password, or force-reset it.\n\n" +
                   "Warning: force reset will lose EFS-encrypted files and Windows Credential Manager entries.",
            Icon = TaskDialogIcon.Warning,
            Buttons = { forceResetBtn, enterPwdBtn, cancelBtn },
            DefaultButton = enterPwdBtn
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
