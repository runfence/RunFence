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
    ICredentialStorePersistence credentialStorePersistence,
    ILoggingService log,
    IPinResetFlowRunner pinResetFlowRunner)
    : IStartupUI
{
    public PinResetResult? PromptNewPin()
    {
        PinResetResult? result = null;
        DialogResult dlgResult = DialogResult.Cancel;

        secureDesktop.Run(() =>
        {
            using var dlg = new PinDialog(PinDialogMode.Set);
            dlg.ProcessingCallback = async (ProtectedString newPin, string? _) =>
            {
                try
                {
                    result = await Task.Run(() => pinService.ResetPin(newPin));
                    credentialStorePersistence.SaveCredentialStore(result.Store);
                    return null;
                }
                catch (Exception ex)
                {
                    result?.Dispose();
                    result = null;
                    return $"PIN creation failed: {ex.Message}";
                }
            };
            dlgResult = dlg.ShowDialog();
        });

        if (dlgResult != DialogResult.OK)
        {
            result?.Dispose();
            return null;
        }

        return result;
    }

    public PinVerifyOutcome PromptVerifyPin(
        CredentialStore store,
        byte[]? configSalt)
    {
        while (true)
        {
            PinVerificationResult? verificationResult = null;
            SecureSecret? mismatchKey = null;
            bool resetRequested = false;
            PinResetResult? resetResult = null;
            DialogResult result = DialogResult.Cancel;

            secureDesktop.Run(() =>
            {
                using var dlg = new PinDialog(PinDialogMode.Verify,
                    configSalt != null
                        ? "Your app configuration was created in a different session. Verification may take a moment longer."
                        : null,
                    exitOnCancel: true);
                dlg.VerifyCallback = (ProtectedString pin) =>
                {
                    verificationResult?.Dispose();
                    verificationResult = pinService.VerifyPinForSession(pin, store);
                    if (!verificationResult.Succeeded)
                        return false;

                    mismatchKey?.Dispose();
                    if (configSalt != null)
                        mismatchKey = pinService.DeriveKeySecret(pin, configSalt);
                    return true;
                };

                result = dlg.ShowDialog();
                if (!dlg.ResetRequested)
                    return;

                resetRequested = true;
                resetResult = pinResetFlowRunner.RunResetFlow();
            });

            if (result == DialogResult.OK)
            {
                var verifiedKey = verificationResult?.TakePinDerivedKey()
                    ?? throw new InvalidOperationException("Secure desktop PIN verification returned OK without a verified key.");
                verificationResult?.Dispose();
                verificationResult = null;
                var ownedMismatchKey = mismatchKey;
                mismatchKey = null;
                return PinVerifyOutcome.Verified(verifiedKey, ownedMismatchKey);
            }

            if (resetResult is { } r)
            {
                verificationResult?.Dispose();
                verificationResult = null;
                mismatchKey?.Dispose();
                mismatchKey = null;
                using (r)
                {
                    return PinVerifyOutcome.Reset(r.Store, r.TakePinDerivedKey());
                }
            }

            if (resetRequested)
            {
                verificationResult?.Dispose();
                verificationResult = null;
                mismatchKey?.Dispose();
                mismatchKey = null;
                continue;
            }

            verificationResult?.Dispose();
            mismatchKey?.Dispose();
            return PinVerifyOutcome.Canceled();
        }
    }

    public RecoveryPinOutcome? PromptRecoveryPin(byte[]? configSalt)
    {
        MessageBox.Show(
            "Your stored account passwords could not be loaded and will be reset.\n\n" +
            "To preserve your existing app configuration, enter the same PIN as before.\n" +
            "If you enter a different PIN, your app configuration will be lost.",
            "Password Reset Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        PinResetResult? resetResult = null;
        SecureSecret? recoveredMismatchKey = null;
        DialogResult dlgResult = DialogResult.Cancel;

        secureDesktop.Run(() =>
        {
            using var dlg = new PinDialog(PinDialogMode.Set);
            dlg.ProcessingCallback = async (ProtectedString newPin, string? _) =>
            {
                try
                {
                    await Task.Run(() =>
                    {
                        resetResult = pinService.ResetPin(newPin);
                        recoveredMismatchKey?.Dispose();
                        recoveredMismatchKey = configSalt != null
                            ? pinService.DeriveKeySecret(newPin, configSalt)
                            : null;
                    });
                    credentialStorePersistence.SaveCredentialStore(resetResult!.Store);
                    log.Info("Credential store recreated after DPAPI loss");
                    return null;
                }
                catch (Exception ex)
                {
                    log.Error("PIN creation failed during recovery", ex);
                    resetResult?.Dispose();
                    resetResult = null;
                    recoveredMismatchKey?.Dispose();
                    recoveredMismatchKey = null;
                    return $"PIN creation failed: {ex.Message}";
                }
            };
            dlgResult = dlg.ShowDialog();
        });

        if (dlgResult != DialogResult.OK)
        {
            resetResult?.Dispose();
            recoveredMismatchKey?.Dispose();
            return null;
        }

        using (resetResult)
        {
            return new RecoveryPinOutcome(
                resetResult!.Store,
                resetResult.TakePinDerivedKey(),
                recoveredMismatchKey);
        }
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
        {
            log.Info("Silent takeover requested via --background mode");
            return true;
        }

        var takeoverResult = MessageBox.Show(
            "RunFence is running in another session. Take over?",
            "Already Running", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        return takeoverResult == DialogResult.Yes;
    }

    public StartupConfigRecoveryChoice ConfirmStartFresh(bool backupAvailable)
    {
        var startFreshButton = new TaskDialogButton("Start Fresh");
        var useBackupButton = new TaskDialogButton("Use Backup");
        var exitButton = new TaskDialogButton("Exit");

        var errorPage = new TaskDialogPage
        {
            Caption = "Configuration Error",
            Heading = "Could not decrypt configuration file.",
            Text = "Your app configuration cannot be read.\n\n" +
                   (backupAvailable
                       ? "\"Use Backup\" will restore the last configuration that loaded successfully.\n" +
                         "\"Start Fresh\" will discard the current configuration and create an empty one."
                       :
                         "\"Start Fresh\" will discard it and create an empty configuration."),
            Icon = TaskDialogIcon.Error,
            DefaultButton = exitButton
        };

        if (backupAvailable)
            errorPage.Buttons.Add(useBackupButton);
        errorPage.Buttons.Add(startFreshButton);
        errorPage.Buttons.Add(exitButton);

        var result = TaskDialog.ShowDialog(errorPage);
        if (result == useBackupButton)
            return StartupConfigRecoveryChoice.UseBackup;
        if (result == startFreshButton)
            return StartupConfigRecoveryChoice.StartFresh;
        return StartupConfigRecoveryChoice.Exit;
    }

    public bool ConfirmRestoreCredentialStoreBackup()
    {
        var useBackupButton = new TaskDialogButton("Use Backup");
        var exitButton = new TaskDialogButton("Exit");

        var errorPage = new TaskDialogPage
        {
            Caption = "Credential Store Error",
            Heading = "Could not read credential store.",
            Text = "The credential store file is corrupt or missing.\n\n" +
                   "\"Use Backup\" will restore the last credential store that loaded successfully.",
            Icon = TaskDialogIcon.Error,
            Buttons = { useBackupButton, exitButton },
            DefaultButton = exitButton
        };

        return TaskDialog.ShowDialog(errorPage) == useBackupButton;
    }

    public MainConfigPinPromptResult PromptMainConfigMismatchPin(
        string configPath,
        Func<ProtectedString, MainConfigPinVerificationResult> verifyPin)
    {
        _ = configPath;
        MainConfigPinVerificationResult verificationResult = MainConfigPinVerificationResult.WrongPin;
        DialogResult dialogResult = DialogResult.Cancel;

        secureDesktop.Run(() =>
        {
            using var dlg = new PinDialog(
                PinDialogMode.Verify,
                "The selected app configuration was encrypted with a different PIN. Enter that config PIN.",
                allowReset: false,
                exitOnCancel: true);
            dlg.VerifyCallback = pin =>
            {
                verificationResult = verifyPin(pin);
                return verificationResult != MainConfigPinVerificationResult.WrongPin;
            };
            dialogResult = dlg.ShowDialog();
        });

        if (dialogResult != DialogResult.OK)
            return MainConfigPinPromptResult.Canceled;

        return verificationResult switch
        {
            MainConfigPinVerificationResult.Verified => MainConfigPinPromptResult.Verified,
            MainConfigPinVerificationResult.AbortToRecovery => MainConfigPinPromptResult.AbortToRecovery,
            _ => MainConfigPinPromptResult.Canceled
        };
    }
}
