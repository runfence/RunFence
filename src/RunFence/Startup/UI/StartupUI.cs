using System.Security.Cryptography;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup.UI.Forms;

namespace RunFence.Startup.UI;

/// <summary>
/// WinForms implementation of <see cref="IStartupUI"/>. Shows PIN dialogs via
/// <see cref="PinDialog"/>, secure desktop via <see cref="ISecureDesktopRunner"/>,
/// and error messages via <see cref="MessageBox"/>.
/// </summary>
public class StartupUI(
    ISecureDesktopRunner secureDesktop,
    IPinService pinService,
    IDatabaseService databaseService,
    ILoggingService log,
    IPinResetFlowRunner pinResetFlowRunner)
    : IStartupUI
{
    public (CredentialStore store, byte[] key)? PromptNewPin()
    {
        CredentialStore? store = null;
        byte[]? key = null;
        DialogResult dlgResult = DialogResult.Cancel;

        secureDesktop.Run(() =>
        {
            using var dlg = new PinDialog(PinDialogMode.Set);
            dlg.ProcessingCallback = async (newPin, _) =>
            {
                try
                {
                    (store, key) = await Task.Run(() => pinService.ResetPin(newPin));
                    databaseService.SaveCredentialStore(store);
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

            secureDesktop.Run(() =>
            {
                using var dlg = new PinDialog(PinDialogMode.Verify,
                    configSalt != null
                        ? "Your app configuration was created in a different session. Verification may take a moment longer."
                        : null);
                dlg.VerifyCallback = pin =>
                {
                    if (!pinService.VerifyPin(pin, store, out var k))
                        return false;
                    verifiedKey = k;
                    if (configSalt != null)
                        mismatchKey = pinService.DeriveKey(pin, configSalt);
                    return true;
                };

                result = dlg.ShowDialog();
                if (!dlg.ResetRequested)
                    return;

                resetRequested = true;
                resetResult = pinResetFlowRunner.RunResetFlow();
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

        secureDesktop.Run(() =>
        {
            using var dlg = new PinDialog(PinDialogMode.Set);
            dlg.ProcessingCallback = async (newPin, _) =>
            {
                try
                {
                    await Task.Run(() =>
                    {
                        (newStore, newKey) = pinService.ResetPin(newPin);
                        recoveredMismatchKey = configSalt != null
                            ? pinService.DeriveKey(newPin, configSalt)
                            : null;
                    });
                    databaseService.SaveCredentialStore(newStore!);
                    log.Info("Credential store recreated after DPAPI loss");
                    return null;
                }
                catch (Exception ex)
                {
                    log.Error("PIN creation failed during recovery", ex);
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

    public bool ConfirmStartFresh()
    {
        var startFreshButton = new TaskDialogButton("Start Fresh");
        var exitButton = new TaskDialogButton("Exit");

        var errorPage = new TaskDialogPage
        {
            Caption = "Configuration Error",
            Heading = "Could not decrypt configuration file.",
            Text = "Your app configuration cannot be read.\n\n" +
                   "\"Start Fresh\" will discard it and create an empty configuration.",
            Icon = TaskDialogIcon.Error,
            Buttons = { startFreshButton, exitButton },
            DefaultButton = exitButton
        };

        var result = TaskDialog.ShowDialog(errorPage);
        return result == startFreshButton;
    }
}