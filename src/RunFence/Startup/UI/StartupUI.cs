using System.Security.Cryptography;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup.UI.Forms;

namespace RunFence.Startup.UI;

/// <summary>
/// WinForms implementation of <see cref="IStartupUI"/>. Shows PIN dialogs via
/// <see cref="PinDialog"/>, secure desktop via <see cref="ISecureDesktopRunner"/>,
/// and error messages via <see cref="MessageBox"/>.
/// </summary>
public class StartupUI : IStartupUI
{
    private readonly ISecureDesktopRunner _secureDesktop;
    private readonly IAppInitializationHelper _appInit;
    private readonly IPinService _pinService;
    private readonly IDatabaseService _databaseService;
    private readonly ILoggingService _log;

    public StartupUI(
        IAppInitializationHelper appInit,
        ISecureDesktopRunner secureDesktop,
        IPinService pinService,
        IDatabaseService databaseService,
        ILoggingService log)
    {
        _secureDesktop = secureDesktop;
        _appInit = appInit;
        _pinService = pinService;
        _databaseService = databaseService;
        _log = log;
    }

    public (CredentialStore store, byte[] key)? PromptNewPin()
    {
        CredentialStore? store = null;
        byte[]? key = null;
        DialogResult dlgResult = DialogResult.Cancel;

        _secureDesktop.Run(() =>
        {
            using var dlg = new PinDialog(PinDialogMode.Set);
            dlg.ProcessingCallback = async (newPin, _) =>
            {
                try
                {
                    (store, key) = await Task.Run(() => _pinService.ResetPin(newPin));
                    _databaseService.SaveCredentialStore(store);
                    return null;
                }
                catch (Exception ex)
                {
                    return $"PIN creation failed: {ex.Message}";
                }
            };
            dlgResult = dlg.ShowDialog();
        });

        if (dlgResult != DialogResult.OK)
            return null;

        return (store!, key!);
    }

    public PinVerifyOutcome PromptVerifyPin(
        CredentialStore store,
        byte[]? configSalt)
    {
        while (true)
        {
            byte[] verifiedKey = Array.Empty<byte>();
            byte[]? mismatchKey = null;
            bool resetRequested = false;
            (CredentialStore Store, byte[] Key)? resetResult = null;
            DialogResult result = DialogResult.Cancel;

            _secureDesktop.Run(() =>
            {
                using var dlg = new PinDialog(PinDialogMode.Verify,
                    configSalt != null
                        ? "Your app configuration was created in a different session. Verification may take a moment longer."
                        : null);
                dlg.VerifyCallback = pin =>
                {
                    if (!_pinService.VerifyPin(pin, store, out var k))
                        return false;
                    verifiedKey = k;
                    if (configSalt != null)
                        mismatchKey = _pinService.DeriveKey(pin, configSalt);
                    return true;
                };

                result = dlg.ShowDialog();
                if (!dlg.ResetRequested)
                    return;

                resetRequested = true;
                resetResult = PinResetFlowRunner.RunResetFlow(_pinService, _databaseService, _appInit);
            });

            if (result == DialogResult.OK)
                return new PinVerifyOutcome(verifiedKey, null, mismatchKey);

            if (resetResult is { } r)
            {
                if (mismatchKey != null)
                    CryptographicOperations.ZeroMemory(mismatchKey);
                return new PinVerifyOutcome(r.Key, r.Store, null);
            }

            if (resetRequested)
                continue;

            if (mismatchKey != null)
                CryptographicOperations.ZeroMemory(mismatchKey);
            return new PinVerifyOutcome(Array.Empty<byte>(), null, null);
        }
    }

    public RecoveryPinOutcome? PromptRecoveryPin(byte[]? configSalt)
    {
        MessageBox.Show(
            "Your stored account passwords could not be loaded and will be reset.\n\n" +
            "To preserve your existing app configuration, enter the same PIN as before.\n" +
            "If you enter a different PIN, your app configuration will be lost.",
            "Password Reset Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        CredentialStore? newStore = null;
        byte[]? newKey = null;
        byte[]? recoveredMismatchKey = null;
        DialogResult dlgResult = DialogResult.Cancel;

        _secureDesktop.Run(() =>
        {
            using var dlg = new PinDialog(PinDialogMode.Set);
            dlg.ProcessingCallback = async (newPin, _) =>
            {
                try
                {
                    await Task.Run(() =>
                    {
                        (newStore, newKey) = _pinService.ResetPin(newPin);
                        recoveredMismatchKey = configSalt != null
                            ? _pinService.DeriveKey(newPin, configSalt)
                            : null;
                    });
                    _databaseService.SaveCredentialStore(newStore!);
                    _log.Info("Credential store recreated after DPAPI loss");
                    return null;
                }
                catch (Exception ex)
                {
                    _log.Error("PIN creation failed during recovery", ex);
                    return $"PIN creation failed: {ex.Message}";
                }
            };
            dlgResult = dlg.ShowDialog();
        });

        if (dlgResult != DialogResult.OK)
            return null;
        return new RecoveryPinOutcome(newStore!, newKey!, recoveredMismatchKey);
    }

    public void ShowError(string message, string title = "Error")
        => MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);

    public bool ConfirmTakeover(bool isFirstRun, bool isBackground)
    {
        if (isFirstRun)
        {
            if (isBackground)
                return false;

            var warnResult = MessageBox.Show(
                "RunFence is running under a different account.\n" +
                "You may be elevated under the wrong account.\n\n" +
                "Click [OK] to take over, or [Cancel] to exit.",
                "Different Account Detected",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            return warnResult == DialogResult.OK;
        }

        if (isBackground)
            return true;

        var takeoverResult = MessageBox.Show(
            "RunFence is running in another session. Take over?",
            "Already Running", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        return takeoverResult == DialogResult.Yes;
    }
}