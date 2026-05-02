using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Security;
using RunFence.Startup.UI.Forms;

namespace RunFence.Account.UI;

/// <summary>
/// Handles read-only password access operations: copy to clipboard and auto-type.
/// Requires PIN or Windows Hello verification before exposing password material.
/// </summary>
public class AccountPasswordAccessHandler(
    ICredentialDecryptionService credentialDecryption,
    ILoggingService log,
    IPinService pinService,
    ISecureDesktopRunner secureDesktop,
    IWindowsHelloService windowsHello,
    ISessionProvider sessionProvider,
    ISecureClipboardService secureClipboard,
    IPasswordAutoTyper autoTyper)
    : IDisposable
{
    private OperationGuard _guard = null!;
    private Control _ownerControl = null!;
    private Action<string> _setStatus = null!;

    // Resolves the parent form for dialogs at call time (form may not be attached at Initialize time).
    private Control Parent => _ownerControl.FindForm() ?? _ownerControl;

    public void Initialize(OperationGuard guard, Control ownerControl, Action<string> setStatus)
    {
        _guard = guard;
        _ownerControl = ownerControl;
        _setStatus = setStatus;
    }

    public async Task CopyPasswordAsync(AccountRow accountRow)
    {
        var session = sessionProvider.GetSession();
        _guard.Begin(Parent);
        ProtectedString? password = null;
        try
        {
            if (!await EnsurePinVerifiedAsync(session, session.CredentialStore))
                return;

            using var copyScope = session.PinDerivedKey.Unprotect();
            var status = credentialDecryption.TryDecryptCredential(accountRow.Sid, session.CredentialStore, copyScope.Data, out _, out password);
            if (status != CredentialLookupStatus.Success || password == null)
            {
                _setStatus("No stored password found.");
                return;
            }

            secureClipboard.CopyProtectedStringToClipboard(password);
            secureClipboard.ScheduleClipboardClear();
            _setStatus("Password copied to clipboard.");
        }
        catch (Exception ex)
        {
            log.Error($"Failed to copy password for {accountRow.Username}", ex);
            MessageBox.Show($"Failed to copy password: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            password?.Dispose();
            _guard.End(Parent);
        }
    }

    public async Task TypePasswordAsync(AccountRow accountRow, IntPtr previousHwnd)
    {
        var session = sessionProvider.GetSession();
        _guard.Begin(Parent);
        ProtectedString? password = null;
        try
        {
            if (previousHwnd == IntPtr.Zero)
            {
                MessageBox.Show("No previously active window found.", "Type Password",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!await EnsurePinVerifiedAsync(session, session.CredentialStore))
                return;

            using var typeScope = session.PinDerivedKey.Unprotect();
            var status = credentialDecryption.TryDecryptCredential(accountRow.Sid, session.CredentialStore, typeScope.Data, out _, out password);
            if (status != CredentialLookupStatus.Success || password == null)
            {
                _setStatus("No stored password found.");
                return;
            }

            switch (autoTyper.TypeToWindow(previousHwnd, password))
            {
                case AutoTypeResult.WindowUnavailable:
                    MessageBox.Show("Target window is no longer available.", "Type Password",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
                case AutoTypeResult.FocusChanged:
                    _setStatus("Typing stopped: focus changed.");
                    break;
                case AutoTypeResult.Success:
                    _setStatus("Password typed.");
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
            _guard.End(Parent);
        }
    }

    private async Task<bool> EnsurePinVerifiedAsync(SessionContext session, CredentialStore store)
    {
        if (session.LastPinVerifiedAt.HasValue
            && (DateTime.UtcNow - session.LastPinVerifiedAt.Value).TotalMinutes < 2)
            return true;

        if (session.Database.Settings.UnlockMode == UnlockMode.WindowsHello)
        {
            var result = await windowsHello.VerifyAsync("Verify your identity to access credentials");
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
            dlg.VerifyCallback = (ProtectedString pin) => pinService.VerifyPin(pin, store, out _);
            if (dlg.ShowDialog() == DialogResult.OK)
                verified = true;
        });

        if (verified)
            session.LastPinVerifiedAt = DateTime.UtcNow;

        return verified;
    }

    public void Dispose()
    {
        secureClipboard.Dispose();
    }
}
