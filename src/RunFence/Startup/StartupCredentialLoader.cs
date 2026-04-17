using System.Security.Cryptography;
using System.Text.Json;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup.UI;

namespace RunFence.Startup;

public class StartupCredentialLoader(
    IStartupUI ui,
    IDatabaseService databaseService,
    IConfigPaths configPaths)
{
    /// <summary>Result of the LoadAndVerifyCredentials step.</summary>
    public record CredentialLoadResult(
        CredentialStore Store,
        byte[] PinDerivedKey,
        byte[]? MismatchKey);

    /// <summary>
    /// Loads the credential store and verifies the PIN. Handles first run, DPAPI loss, and
    /// JSON corruption cases. Returns null if the user cancelled or an unrecoverable error occurred.
    /// </summary>
    public CredentialLoadResult? LoadAndVerifyCredentials(
        ICredentialEncryptionService encryptionService,
        ILoggingService log)
    {
        log.Info("StartupCredentialLoader: loading credentials.");
        var configSalt = databaseService.TryGetConfigSalt();

        try
        {
            var credentialStore = databaseService.LoadCredentialStore();

            var mismatchConfigSalt = configSalt != null &&
                                     !configSalt.SequenceEqual(credentialStore.ArgonSalt)
                ? configSalt
                : null;

            var pinResult = ui.PromptVerifyPin(credentialStore, mismatchConfigSalt);
            if (pinResult.Key.Length == 0)
                return null;

            if (pinResult.NewStore != null)
            {
                log.Info("PIN reset from verification dialog");
                credentialStore = pinResult.NewStore;
            }

            var pinDerivedKey = pinResult.Key;
            var mismatchKey = pinResult.MismatchKey;
            bool success = false;

            try
            {
                if (!VerifyDpapiAccess(credentialStore, pinDerivedKey, encryptionService, log))
                {
                    CryptographicOperations.ZeroMemory(pinDerivedKey);
                    if (mismatchKey != null)
                    {
                        CryptographicOperations.ZeroMemory(mismatchKey);
                        mismatchKey = null;
                    }

                    try
                    {
                        File.Delete(configPaths.CredentialsFilePath);
                    }
                    catch
                    {
                    }

                    var recovery = ui.PromptRecoveryPin(configSalt);
                    if (recovery == null)
                        return null;
                    credentialStore = recovery.Value.Store;
                    pinDerivedKey = recovery.Value.Key;
                    mismatchKey = recovery.Value.MismatchKey;
                }

                success = true;
                log.Info("StartupCredentialLoader: credentials loaded and verified.");
                return new CredentialLoadResult(credentialStore, pinDerivedKey, mismatchKey);
            }
            finally
            {
                if (!success)
                {
                    CryptographicOperations.ZeroMemory(pinDerivedKey);
                    if (mismatchKey != null)
                        CryptographicOperations.ZeroMemory(mismatchKey);
                }
            }
        }
        catch (FileNotFoundException)
        {
            var result = ui.PromptNewPin();
            if (result == null)
                return null;
            log.Info("StartupCredentialLoader: credentials created (first run).");
            return new CredentialLoadResult(result.Value.store, result.Value.key, null);
        }
        catch (JsonException ex)
        {
            log.Error("Credential store corrupt", ex);
            ui.ShowError("Credential store file is corrupt. Backup at credentials.bak may help.");
            return null;
        }
        catch (IOException ex)
        {
            log.Error("Credential store inaccessible", ex);
            ui.ShowError($"Credential store inaccessible: {ex.Message}");
            return null;
        }
    }

    private static bool VerifyDpapiAccess(CredentialStore store, byte[] pinDerivedKey,
        ICredentialEncryptionService encryptionService, ILoggingService log)
    {
        var cred = store.Credentials.FirstOrDefault(c => c is { IsCurrentAccount: false, EncryptedPassword.Length: > 0 });
        if (cred == null)
            return true;

        try
        {
            using var password = encryptionService.Decrypt(cred.EncryptedPassword, pinDerivedKey);
            return true;
        }
        catch (CryptographicException ex)
        {
            log.Error("DPAPI key loss detected — credential decryption failed after successful PIN verification", ex);
            return false;
        }
    }
}