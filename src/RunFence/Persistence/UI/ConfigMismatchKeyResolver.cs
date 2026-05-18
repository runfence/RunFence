using System.Security.Cryptography;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Security;
using RunFence.Startup.UI.Forms;

namespace RunFence.Persistence.UI;

/// <summary>
/// Derives a decryption key when an additional config file was encrypted with a different PIN.
/// Prompts the user for the old PIN via the secure desktop, then verifies it against the config file.
/// </summary>
public class ConfigMismatchKeyResolver(
    ISessionProvider sessionProvider,
    IDatabaseService databaseService,
    ConfigMismatchPinVerifier configMismatchPinVerifier,
    ISecureDesktopRunner secureDesktop,
    OperationGuard enforcementGuard)
{
    private Control? _guardOwner;

    /// <summary>
    /// Provides the UI control used to disable the UI during PIN entry.
    /// Call after the host form is created to complete wiring.
    /// </summary>
    public void Initialize(Control guardOwner)
    {
        _guardOwner = guardOwner;
    }

    /// <summary>
    /// Returns a derived key if the config file's Argon salt differs from the current session salt,
    /// or null if no mismatch. Throws <see cref="OperationCanceledException"/> if the user cancels the PIN dialog.
    /// </summary>
    public SecureSecret? TryDeriveConfigMismatchKey(string configPath)
    {
        var session = sessionProvider.GetSession();
        var fileSalt = databaseService.TryGetAppConfigSaltFromPath(configPath);
        if (fileSalt == null || fileSalt.SequenceEqual(session.CredentialStore.ArgonSalt))
            return null;
        return PromptForMismatchKey(
            pin => configMismatchPinVerifier.VerifyAndReturnKey(
                pin,
                fileSalt,
                candidate => databaseService.LoadAppConfigFromPath(configPath, candidate)));
    }

    public AppConfigMismatchLoadResult? TryLoadAppConfigWithMismatchKey(string configPath, bool forcePrompt = false)
    {
        var session = sessionProvider.GetSession();
        var fileSalt = databaseService.TryGetAppConfigSaltFromPath(configPath);
        bool shouldPrompt = false;
        if (fileSalt != null)
        {
            shouldPrompt = forcePrompt || !fileSalt.SequenceEqual(session.CredentialStore.ArgonSalt);
        }

        if (!shouldPrompt)
            return null;

        AppConfigMismatchLoadResult? result = null;
        PromptForMismatchKey(
            pin => configMismatchPinVerifier.VerifyTemporary(
                pin,
                fileSalt!,
                candidate =>
                {
                    result = new AppConfigMismatchLoadResult(
                        databaseService.LoadAppConfigFromPath(configPath, candidate),
                        UsedMismatchKey: true);
                }));
        return result;
    }

    private SecureSecret? PromptForMismatchKey(
        Func<ProtectedString, ConfigMismatchPinVerificationResult> verifyCandidate)
    {
        SecureSecret? verifiedKey = null;
        Exception? fatalLoadException = null;
        DialogResult pinResult = DialogResult.Cancel;

        if (_guardOwner != null)
            enforcementGuard.Begin(_guardOwner);
        else
            enforcementGuard.Begin();
        try
        {
            secureDesktop.Run(() =>
            {
                using var dlg = new PinDialog(PinDialogMode.Verify,
                    "This config could not be decrypted with your current PIN. Enter the PIN used to encrypt it.\n" +
                    "It will then be re-encrypted with your current PIN.");
                dlg.VerifyCallback = pin =>
                {
                    try
                    {
                        using var verification = verifyCandidate(pin);
                        switch (verification.Status)
                        {
                            case ConfigMismatchPinVerificationResult.StatusKind.WrongPin:
                                verifiedKey?.Dispose();
                                verifiedKey = null;
                                fatalLoadException = null;
                                return false;

                            case ConfigMismatchPinVerificationResult.StatusKind.VerifiedTemporaryOnly:
                                verifiedKey?.Dispose();
                                verifiedKey = null;
                                fatalLoadException = null;
                                return true;

                            case ConfigMismatchPinVerificationResult.StatusKind.VerifiedWithReturnedKey:
                                verifiedKey?.Dispose();
                                verifiedKey = verification.TakeVerifiedKey("The verified mismatch key was already taken.");
                                fatalLoadException = null;
                                return true;

                            case ConfigMismatchPinVerificationResult.StatusKind.AbortToRecovery:
                                verifiedKey?.Dispose();
                                verifiedKey = null;
                                fatalLoadException = verification.FatalException
                                    ?? new InvalidOperationException("Config mismatch PIN verification aborted without a fatal exception.");
                                return true;

                            default:
                                throw new InvalidOperationException($"Unknown mismatch verification status: {verification.Status}");
                        }
                    }
                    catch (Exception ex)
                    {
                        verifiedKey?.Dispose();
                        verifiedKey = null;
                        fatalLoadException = ex;
                        return true;
                    }
                };
                pinResult = dlg.ShowDialog();
            });
        }
        finally
        {
            if (_guardOwner != null)
                enforcementGuard.End(_guardOwner);
            else
                enforcementGuard.End();
        }

        if (fatalLoadException != null)
        {
            verifiedKey?.Dispose();
            throw fatalLoadException;
        }

        if (pinResult != DialogResult.OK)
        {
            verifiedKey?.Dispose();
            throw new OperationCanceledException();
        }

        return verifiedKey;
    }
}
