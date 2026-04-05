using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup.UI.Forms;
using RunFence.UI.Forms;
using Timer = System.Windows.Forms.Timer;

namespace RunFence.Account.UI;

public class AccountPasswordHandler(
    IAccountCredentialManager credentialManager,
    IAccountPasswordService accountPassword,
    IPinService pinService,
    ILoggingService log,
    SidDisplayNameResolver displayNameResolver,
    IPasswordAutoTyper autoTyper,
    ISecureDesktopRunner secureDesktop,
    IWindowsHelloService windowsHello,
    IDatabaseProvider databaseProvider)
    : IDisposable
{
    private enum NoStoredPasswordChoice
    {
        Cancel,
        ForceReset,
        EnterCurrentPassword
    }

    private Timer? _clipboardClearTimer;
    private byte[]? _clipboardExpectedHash;

    public void CopyPassword(AccountRow accountRow, SessionContext session,
        CredentialStore store, OperationGuard guard, Control parent, Action<string> setStatus)
    {
        guard.Begin(parent);
        SecureString? password = null;
        try
        {
            if (!EnsurePinVerified(session, store))
                return;

            var status = credentialManager.DecryptCredential(accountRow.Sid, store, session.PinDerivedKey, out password);
            if (status != CredentialLookupStatus.Success || password == null)
            {
                MessageBox.Show("No stored password found.", "Copy Password", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            CopySecureStringToClipboard(password);
            ScheduleClipboardClear();
            setStatus("Password copied to clipboard.");
        }
        catch (Exception ex)
        {
            log.Error($"Failed to copy password for {accountRow.Username}", ex);
            MessageBox.Show($"Failed to copy password: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            password?.Dispose();
            guard.End(parent);
        }
    }

    public void TypePassword(AccountRow accountRow, SessionContext session,
        CredentialStore store, OperationGuard guard, Control parent, IntPtr previousHwnd,
        Action<string> setStatus)
    {
        guard.Begin(parent);
        SecureString? password = null;
        try
        {
            if (previousHwnd == IntPtr.Zero)
            {
                MessageBox.Show("No previously active window found.", "Type Password",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!EnsurePinVerified(session, store))
                return;

            var status = credentialManager.DecryptCredential(accountRow.Sid, store, session.PinDerivedKey, out password);
            if (status != CredentialLookupStatus.Success || password == null)
            {
                MessageBox.Show("No stored password found.", "Type Password",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            switch (autoTyper.TypeToWindow(previousHwnd, password))
            {
                case AutoTypeResult.WindowUnavailable:
                    MessageBox.Show("Target window is no longer available.", "Type Password",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
                case AutoTypeResult.FocusChanged:
                    setStatus("Typing stopped: focus changed.");
                    break;
                case AutoTypeResult.Success:
                    setStatus("Password typed.");
                    break;
            }
        }
        catch (Exception ex)
        {
            log.Error($"Failed to type password for {accountRow.Username}", ex);
            MessageBox.Show($"Failed to type password: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            password?.Dispose();
            guard.End(parent);
        }
    }

    public void RotatePassword(AccountRow accountRow, SessionContext session,
        CredentialStore store, OperationGuard guard, Control parent,
        Action<string> setStatus, Action<Guid?> save)
    {
        guard.Begin(parent);
        SecureString? oldPassword = null;
        var newPasswordChars = Array.Empty<char>();
        try
        {
            newPasswordChars = PasswordHelper.GenerateRandomPassword();
            var status = credentialManager.DecryptCredential(accountRow.Sid, store, session.PinDerivedKey, out oldPassword);

            bool changed;
            if (status == CredentialLookupStatus.Success && oldPassword != null)
                changed = TryChangePassword(accountRow.Sid, accountRow.Username, oldPassword, new string(newPasswordChars), parent);
            else
                changed = TryChangePasswordNoStoredCredential(accountRow.Sid, accountRow.Username, new string(newPasswordChars), parent);

            if (!changed)
                return;

            if (accountRow.Credential != null)
            {
                using var newPassword = new SecureString();
                foreach (var c in newPasswordChars)
                    newPassword.AppendChar(c);
                newPassword.MakeReadOnly();
                credentialManager.UpdateCredentialPassword(accountRow.Credential, newPassword, session.PinDerivedKey);
                save(accountRow.Credential.Id);
            }
            else
            {
                save(null);
            }

            setStatus("Password rotated successfully.");
        }
        catch (Exception ex)
        {
            log.Error($"Failed to rotate password for {accountRow.Username}", ex);
            MessageBox.Show($"Failed to rotate password: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Array.Clear(newPasswordChars, 0, newPasswordChars.Length);
            oldPassword?.Dispose();
            guard.End(parent);
        }
    }

    public void SetEmptyPassword(AccountRow accountRow, SessionContext session,
        CredentialStore store, OperationGuard guard, Control parent,
        Action<string> setStatus, Action<Guid?> save)
    {
        var database = databaseProvider.GetDatabase();
        var displayName = accountRow.Credential != null
            ? displayNameResolver.GetDisplayName(accountRow.Credential, database.SidNames)
            : displayNameResolver.GetDisplayName(accountRow.Sid, accountRow.Username, database.SidNames);
        if (MessageBox.Show(
                $"Set empty password for \u201C{displayName}\u201D?\n\nThe account will be usable without a password.",
                "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        guard.Begin(parent);
        SecureString? oldPassword = null;
        try
        {
            var status = credentialManager.DecryptCredential(accountRow.Sid, store, session.PinDerivedKey, out oldPassword);

            bool changed;
            if (status == CredentialLookupStatus.Success && oldPassword != null)
                changed = TryChangePassword(accountRow.Sid, accountRow.Username, oldPassword, "", parent);
            else
                changed = TryChangePasswordNoStoredCredential(accountRow.Sid, accountRow.Username, "", parent);

            if (!changed)
                return;

            if (accountRow.Credential != null)
            {
                using var emptyPassword = new SecureString();
                emptyPassword.MakeReadOnly();
                credentialManager.UpdateCredentialPassword(accountRow.Credential, emptyPassword, session.PinDerivedKey);
                save(accountRow.Credential.Id);
            }
            else
            {
                save(null);
            }

            setStatus("Password set to empty.");
        }
        catch (Exception ex)
        {
            log.Error($"Failed to set empty password for {accountRow.Username}", ex);
            MessageBox.Show($"Failed to set empty password: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            oldPassword?.Dispose();
            guard.End(parent);
        }
    }

    // Returns true if the password was changed, false if cancelled.
    // Other exceptions propagate to the caller's catch block.
    private bool TryChangePassword(string sid, string username, SecureString oldPassword, string newPassword, Control parent)
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
    private bool TryChangePasswordNoStoredCredential(string sid, string username, string newPassword, Control parent)
    {
        using (var emptyPwd = new SecureString())
        {
            emptyPwd.MakeReadOnly();
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

    private static SecureString? PromptCurrentPassword(string username)
    {
        DialogResult result = DialogResult.None;
        SecureString? password = null;

        DataPanel.RunOnSecureDesktop(() =>
        {
            using var dlg = new PasswordInputDialog(username);
            result = dlg.ShowDialog();
            password = dlg.Password;
        });

        return result == DialogResult.OK ? password : null;
    }

    private bool EnsurePinVerified(SessionContext session, CredentialStore store)
    {
        if (session.LastPinVerifiedAt.HasValue
            && (DateTime.UtcNow - session.LastPinVerifiedAt.Value).TotalMinutes < 2)
            return true;

        if (session.Database.Settings.UnlockMode == UnlockMode.WindowsHello)
        {
            var result = windowsHello.VerifySync("Verify your identity to access credentials");
            switch (result)
            {
                case HelloVerificationResult.Verified:
                    session.LastPinVerifiedAt = DateTime.UtcNow;
                    log.Info("PIN verification via Windows Hello succeeded");
                    return true;
                case HelloVerificationResult.Canceled:
                    log.Info("Windows Hello verification canceled by user, PIN required");
                    break;
                case HelloVerificationResult.NotAvailable:
                    log.Warn("Windows Hello not available for current account, using PIN instead");
                    break;
                case HelloVerificationResult.Failed:
                    log.Error("Windows Hello verification failed for current account, using PIN instead");
                    break;
            }
        }

        bool verified = false;
        secureDesktop.Run(() =>
        {
            using var dlg = new PinDialog(PinDialogMode.Verify);
            dlg.VerifyCallback = pin => pinService.VerifyPin(pin, store, out _);
            if (dlg.ShowDialog() == DialogResult.OK)
                verified = true;
        });

        if (verified)
            session.LastPinVerifiedAt = DateTime.UtcNow;

        return verified;
    }

    private static void CopySecureStringToClipboard(SecureString password)
    {
        var ptr = Marshal.SecureStringToGlobalAllocUnicode(password);
        try
        {
            var text = Marshal.PtrToStringUni(ptr)!;
            var dataObject = new DataObject(DataFormats.UnicodeText, text);
            dataObject.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
            Clipboard.SetDataObject(dataObject, copy: true);
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }

    private void ScheduleClipboardClear()
    {
        _clipboardClearTimer?.Dispose();
        _clipboardClearTimer = null;

        _clipboardExpectedHash = SHA256.HashData(Encoding.Unicode.GetBytes(Clipboard.GetText()));

        _clipboardClearTimer = new Timer { Interval = 60_000 };
        _clipboardClearTimer.Tick += OnClipboardClearTimerTick;
        _clipboardClearTimer.Start();
    }

    private void OnClipboardClearTimerTick(object? sender, EventArgs e)
    {
        if (Clipboard.ContainsText() && _clipboardExpectedHash != null &&
            SHA256.HashData(Encoding.Unicode.GetBytes(Clipboard.GetText()))
                .AsSpan().SequenceEqual(_clipboardExpectedHash))
            Clipboard.Clear();

        _clipboardExpectedHash = null;
        _clipboardClearTimer?.Dispose();
        _clipboardClearTimer = null;
    }

    public void Dispose()
    {
        _clipboardClearTimer?.Dispose();
        _clipboardClearTimer = null;
    }
}