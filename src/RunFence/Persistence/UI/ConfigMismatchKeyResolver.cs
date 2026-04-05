using System.Security.Cryptography;
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
    IPinService pinService,
    IDatabaseService databaseService,
    ISecureDesktopRunner secureDesktop,
    ApplicationState? applicationState)
{
    private Control? _guardOwner;

    public void SetGuardOwner(Control guardOwner)
    {
        _guardOwner = guardOwner;
    }

    /// <summary>
    /// Returns a derived key if the config file's Argon salt differs from the current session salt,
    /// or null if no mismatch. Throws <see cref="OperationCanceledException"/> if the user cancels the PIN dialog.
    /// The caller is responsible for zeroing the returned key when done.
    /// </summary>
    public byte[]? TryDeriveConfigMismatchKey(string configPath)
    {
        var session = sessionProvider.GetSession();
        var fileSalt = databaseService.TryGetAppConfigSalt(configPath);
        if (fileSalt == null || fileSalt.SequenceEqual(session.CredentialStore.ArgonSalt))
            return null;

        byte[]? mismatchKey = null;
        DialogResult pinResult = DialogResult.Cancel;

        var guard = applicationState?.EnforcementGuard;
        if (guard != null && _guardOwner != null)
            guard.Begin(_guardOwner);
        else
            guard?.Begin();
        try
        {
            secureDesktop.Run(() =>
            {
                using var dlg = new PinDialog(PinDialogMode.Verify,
                    "This config was encrypted with a different PIN. Enter that PIN to decrypt it.\n" +
                    "It will then be re-encrypted with your current PIN.");
                dlg.VerifyCallback = pin =>
                {
                    var key = pinService.DeriveKey(pin, fileSalt);
                    try
                    {
                        databaseService.LoadAppConfig(configPath, key);
                        mismatchKey = key;
                        return true;
                    }
                    catch (Exception)
                    {
                        CryptographicOperations.ZeroMemory(key);
                        return false;
                    }
                };
                pinResult = dlg.ShowDialog();
            });
        }
        finally
        {
            if (guard != null && _guardOwner != null)
                guard.End(_guardOwner);
            else
                guard?.End();
        }

        if (pinResult != DialogResult.OK)
        {
            if (mismatchKey != null)
                CryptographicOperations.ZeroMemory(mismatchKey);
            throw new OperationCanceledException();
        }

        return mismatchKey;
    }
}