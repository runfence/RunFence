using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup.UI;

namespace RunFence.Startup;

public class StartupCredentialLoader(
    IStartupUI ui,
    ICredentialStorePersistence credentialStorePersistence,
    IConfigSaltReader configSaltReader,
    ILoadedGoodBackupStore loadedGoodBackupStore,
    IConfigPaths configPaths,
    IRememberPinService rememberPinService,
    IPinService pinService,
    ICredentialEncryptionSpanService encryptionService,
    ILoggingService log) : IStartupCredentialLoader
{
    /// <summary>
    /// Loads the credential store and verifies the PIN. Handles first run, DPAPI loss, and
    /// JSON corruption cases. Returns null if the user cancelled or an unrecoverable error occurred.
    /// </summary>
    public CredentialLoadResult? LoadAndVerifyCredentials(string selectedConfigPath)
        => LoadAndVerifyCredentials(
            selectedConfigPath,
            credentialBackupLoadAttempted: false,
            credentialStorePath: configPaths.CredentialsFilePath,
            usingBackupSource: false);

    private CredentialLoadResult? LoadAndVerifyCredentials(
        string selectedConfigPath,
        bool credentialBackupLoadAttempted,
        string credentialStorePath,
        bool usingBackupSource)
    {
        log.Info("StartupCredentialLoader: loading credentials.");
        var configSalt = configSaltReader.TryGetConfigSaltFromPath(selectedConfigPath);

        try
        {
            var credentialStore = usingBackupSource
                ? credentialStorePersistence.LoadCredentialStoreFromPath(credentialStorePath)
                : credentialStorePersistence.LoadCredentialStore();

            var configSaltMatchesCredentialStore = configSalt != null &&
                                                   configSalt.SequenceEqual(credentialStore.ArgonSalt);
            var requiresInteractivePinForMismatch = configSalt != null && !configSaltMatchesCredentialStore;
            var mismatchConfigSalt = configSalt != null &&
                                     !configSaltMatchesCredentialStore
                ? configSalt
                : null;

            if (rememberPinService.IsEnabled)
            {
                if (rememberPinService.TryDecryptSecret(out var rememberedPinKey))
                {
                    Debug.Assert(rememberedPinKey != null);
                    bool rememberPinCanaryVerified = false;
                    if (rememberedPinKey!.TransformSnapshot(key => pinService.VerifyDerivedKey(key, credentialStore)))
                    {
                        rememberPinCanaryVerified = true;
                        if (!requiresInteractivePinForMismatch)
                        {
                            return AcceptCredentialStore(
                                credentialStore,
                                rememberedPinKey,
                                null,
                                usingBackupSource,
                                configSalt,
                                pinBypassed: true);
                        }

                        log.Info("Remember PIN bypass skipped because config salt mismatched");
                    }

                    if (rememberPinCanaryVerified)
                        log.Info("Remember PIN key verified but startup requires explicit PIN verification");
                    else
                        log.Warn("Remember PIN key failed canary verification - falling back to PIN");
                    rememberedPinKey.Dispose();
                }
                else
                {
                    log.Info("Remember PIN key unavailable - falling back to PIN");
                }
            }

            using var pinResult = ui.PromptVerifyPin(credentialStore, mismatchConfigSalt) ?? PinVerifyOutcome.Canceled();
            if (pinResult.IsCanceled)
                return null;

            if (pinResult.NewStore != null)
            {
                log.Info("PIN reset from verification dialog");
                credentialStore = pinResult.NewStore;
            }

            SecureSecret? pinDerivedKey = null;
            SecureSecret? mismatchKey = null;
            bool success = false;

            try
            {
                pinDerivedKey = pinResult.TakeKey();
                mismatchKey = pinResult.TakeMismatchKey();
                var acceptedResult = AcceptCredentialStore(
                    credentialStore,
                    pinDerivedKey,
                    mismatchKey,
                    usingBackupSource,
                    configSalt,
                    pinBypassed: false);
                if (acceptedResult == null)
                    return null;

                success = true;
                pinDerivedKey = null;
                mismatchKey = null;
                return acceptedResult;
            }
            finally
            {
                if (!success)
                {
                    pinDerivedKey?.Dispose();
                    mismatchKey?.Dispose();
                }
            }
        }
        catch (FileNotFoundException)
        {
            if (TryContinueWithCredentialBackup(
                    selectedConfigPath,
                    credentialBackupLoadAttempted,
                    out var backupResult))
            {
                return backupResult;
            }

            var result = ui.PromptNewPin();
            if (result == null)
                return null;
            log.Info("StartupCredentialLoader: credentials created (first run).");
            using (result)
            {
                return new CredentialLoadResult(result.Store, result.TakePinDerivedKey(), null);
            }
        }
        catch (JsonException ex)
        {
            log.Error("Credential store corrupt", ex);
            if (TryContinueWithCredentialBackup(
                    selectedConfigPath,
                    credentialBackupLoadAttempted,
                    out var backupResult))
            {
                return backupResult;
            }

            ui.ShowError("Credential store file is corrupt.");
            return null;
        }
        catch (IOException ex)
        {
            log.Error("Credential store inaccessible", ex);
            if (TryContinueWithCredentialBackup(
                    selectedConfigPath,
                    credentialBackupLoadAttempted,
                    out var backupResult))
            {
                return backupResult;
            }

            ui.ShowError($"Credential store inaccessible: {ex.Message}");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            log.Error("Credential store inaccessible", ex);
            if (TryContinueWithCredentialBackup(
                    selectedConfigPath,
                    credentialBackupLoadAttempted,
                    out var backupResult))
            {
                return backupResult;
            }

            ui.ShowError($"Credential store inaccessible: {ex.Message}");
            return null;
        }
    }

    private bool TryContinueWithCredentialBackup(
        string selectedConfigPath,
        bool credentialBackupLoadAttempted,
        out CredentialLoadResult? backupResult)
    {
        backupResult = null;
        if (credentialBackupLoadAttempted ||
            !loadedGoodBackupStore.Exists(configPaths.CredentialsFilePath) ||
            !ui.ConfirmRestoreCredentialStoreBackup())
        {
            return false;
        }

        var backupPath = loadedGoodBackupStore.GetBackupPath(configPaths.CredentialsFilePath);
        backupResult = LoadAndVerifyCredentials(
            selectedConfigPath,
            credentialBackupLoadAttempted: true,
            credentialStorePath: backupPath,
            usingBackupSource: true);
        return true;
    }

    private CredentialLoadResult? AcceptCredentialStore(
        CredentialStore credentialStore,
        SecureSecret pinDerivedKey,
        SecureSecret? mismatchKey,
        bool usingBackupSource,
        byte[]? configSalt,
        bool pinBypassed)
    {
        var success = false;
        try
        {
            if (!VerifyDpapiAccess(credentialStore, pinDerivedKey))
            {
                pinDerivedKey.Dispose();
                pinDerivedKey = null!;
                mismatchKey?.Dispose();
                mismatchKey = null;

                try
                {
                    File.Delete(configPaths.CredentialsFilePath);
                }
                catch
                {
                }

                using var recovery = ui.PromptRecoveryPin(configSalt);
                if (recovery == null)
                    return null;

                credentialStore = recovery.Store;
                pinDerivedKey = recovery.TakeKey();
                mismatchKey = recovery.TakeMismatchKey();
                pinBypassed = false;
            }

            if (usingBackupSource)
                credentialStorePersistence.SaveCredentialStore(credentialStore);

            success = true;
            log.Info("StartupCredentialLoader: credentials loaded and verified.");
            return new CredentialLoadResult(credentialStore, pinDerivedKey, mismatchKey, pinBypassed);
        }
        finally
        {
            if (!success)
            {
                pinDerivedKey?.Dispose();
                mismatchKey?.Dispose();
            }
        }
    }

    private bool VerifyDpapiAccess(CredentialStore store, ISecureSecretSnapshotSource pinDerivedKey)
    {
        var encryptedCredentials = store.Credentials
            .Where(c => c is { IsCurrentAccount: false, EncryptedPassword.Length: > 0 })
            .ToList();
        if (encryptedCredentials.Count == 0)
            return true;

        bool allAccessible = true;
        try
        {
            foreach (var cred in encryptedCredentials)
            {
                try
                {
                    using var password = pinDerivedKey.TransformSnapshot(key => encryptionService.Decrypt(cred.EncryptedPassword, key));
                }
                catch (CryptographicException ex)
                {
                    allAccessible = false;
                    log.Error($"DPAPI key loss detected for credential SID {cred.Sid}", ex);
                }
            }

            return allAccessible;
        }
        finally
        {
            log.Info($"DPAPI credential classification complete: checked={encryptedCredentials.Count}, allAccessible={allAccessible}");
        }
    }
}
