using System.Security.Cryptography;
using System.Security.Principal;
using System.Diagnostics;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup.UI;

namespace RunFence.Startup;

/// <summary>
/// Foundation-scope service that orchestrates the full startup sequence from credential
/// loading through UI exit. <c>Program.Main</c> keeps process/bootstrap concerns
/// (exception logging, WinForms setup, elevation, privileges, mutex) and delegates
/// the nested session/config/UI lifecycle to this orchestrator.
///
/// Exit codes returned by <see cref="Run"/>:
/// <list type="bullet">
///   <item>0 - normal exit</item>
///   <item>-4 - credential load cancelled or failed</item>
/// </list>
/// Config decryption failure with Start Fresh rejected: returns 0 without entering the
/// session scope (caller exits normally; user is informed via <see cref="IStartupUI.ConfirmStartFresh"/>).
/// </summary>
public class StartupOrchestrator(
    ILoggingService log,
    IDatabaseService databaseService,
    IConfigPaths configPaths,
    ILoadedGoodBackupStore loadedGoodBackupStore,
    IAppInitializationHelper appInit,
    IRememberPinService rememberPinService,
    ConfigMismatchPinVerifier configMismatchPinVerifier,
    IStartupUI startupUi,
    IStartupSessionScopeFactory sessionScopeFactory,
    IStartupMainFormRunner mainFormRunner,
    IStartupCredentialLoader credentialLoader)
{
    private sealed record StartupConfigLoadSource(
        string SourcePath,
        string PersistencePath,
        bool LoadFromPath);

    /// <summary>
    /// Runs the full startup sequence and returns the process exit code.
    /// </summary>
    /// <param name="isBackground">
    /// Whether the application was started with <c>--background</c>.
    /// </param>
    public int Run(bool isBackground, bool grantStartupRunAsUnlock = false)
    {
        using var identity = WindowsIdentity.GetCurrent();
        log.Info($"Running as: {identity.Name}");

        var primarySource = new StartupConfigLoadSource(
            configPaths.ConfigFilePath,
            configPaths.ConfigFilePath,
            LoadFromPath: false);
        var backupSource = new StartupConfigLoadSource(
            loadedGoodBackupStore.GetBackupPath(configPaths.ConfigFilePath),
            configPaths.ConfigFilePath,
            LoadFromPath: true);

        var selectedSource = primarySource;
        while (true)
        {
            var selectedConfigSalt = databaseService.TryGetConfigSaltFromPath(selectedSource.SourcePath);
            var credResult = credentialLoader.LoadAndVerifyCredentials(selectedSource.SourcePath);
            if (credResult == null)
                return -4;

            using var ownedCredentialResult = credResult;
            SecureSecret? sessionPinDerivedKey = null;
            SecureSecret? mismatchKey = null;
            try
            {
                sessionPinDerivedKey = ownedCredentialResult.TakePinDerivedKey();
                mismatchKey = ownedCredentialResult.TakeMismatchKey();
                var credentialStore = credResult.Store;

                AppDatabase? database = null;
                var selectedSourceNeedsSave = !string.Equals(
                    selectedSource.SourcePath,
                    selectedSource.PersistencePath,
                    StringComparison.OrdinalIgnoreCase);
                var forceCredentialAndConfigSave = false;

                if (mismatchKey != null)
                {
                    try
                    {
                        database = LoadSelectedConfigWithMismatchKey(selectedSource, mismatchKey);
                        selectedSourceNeedsSave = true;
                    }
                    catch (Exception ex) when (IsConfigKeyFailure(ex) && selectedConfigSalt != null)
                    {
                        if (!TryPromptForSelectedConfigPin(
                                selectedSource,
                                selectedConfigSalt,
                                out database))
                        {
                            var recoveryAction = ChooseRecoveryAction(selectedSource);
                            if (recoveryAction == StartupRecoveryAction.UseBackup)
                            {
                                selectedSource = backupSource;
                                continue;
                            }

                            if (recoveryAction == StartupRecoveryAction.Exit)
                                return 0;

                            database = CreateNewDatabase(credentialStore);
                            selectedSourceNeedsSave = false;
                            forceCredentialAndConfigSave = true;
                        }
                        else
                        {
                            selectedSourceNeedsSave = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error($"Failed to load selected config from {selectedSource.SourcePath}", ex);
                        var recoveryAction = ChooseRecoveryAction(selectedSource);
                        if (recoveryAction == StartupRecoveryAction.UseBackup)
                        {
                            selectedSource = backupSource;
                            continue;
                        }

                        if (recoveryAction == StartupRecoveryAction.Exit)
                            return 0;

                        database = CreateNewDatabase(credentialStore);
                        selectedSourceNeedsSave = false;
                        forceCredentialAndConfigSave = true;
                    }
                    finally
                    {
                        mismatchKey.Dispose();
                        mismatchKey = null;
                    }
                }

                if (database == null)
                {
                    var loadResult = LoadSelectedConfigWithSessionKey(
                        selectedSource,
                        sessionPinDerivedKey,
                        selectedConfigSalt);

                    switch (loadResult.Action)
                    {
                        case StartupRecoveryAction.Loaded:
                            database = loadResult.Database!;
                            selectedSourceNeedsSave = selectedSourceNeedsSave || loadResult.UsedMismatchRecovery;
                            break;

                        case StartupRecoveryAction.UseBackup:
                            selectedSource = backupSource;
                            continue;

                        case StartupRecoveryAction.Exit:
                            return 0;

                        default:
                            database = CreateNewDatabase(credentialStore);
                            selectedSourceNeedsSave = false;
                            forceCredentialAndConfigSave = true;
                            break;
                    }
                }

                Debug.Assert(database != null);

                var currentSid = SidResolutionHelper.GetCurrentUserSid();
                bool needsSave = false;
                if (!forceCredentialAndConfigSave)
                    needsSave = appInit.EnsureCurrentAccountCredential(credentialStore, database);
                appInit.EnsureInteractiveUserSidName(database);
                needsSave |= appInit.NormalizeAccountSids(database.Apps, currentSid);

                if (forceCredentialAndConfigSave || needsSave)
                    databaseService.SaveCredentialStoreAndConfig(credentialStore, database, sessionPinDerivedKey);
                else if (selectedSourceNeedsSave)
                    databaseService.SaveConfig(database, sessionPinDerivedKey, credentialStore.ArgonSalt);

                TryPreserveAcceptedStartupFile(configPaths.CredentialsFilePath);
                TryPreserveAcceptedStartupFile(configPaths.ConfigFilePath);

                foreach (var group in credentialStore.Credentials
                             .Where(c => !string.IsNullOrEmpty(c.Sid))
                             .GroupBy(c => c.Sid, StringComparer.OrdinalIgnoreCase)
                             .Where(g => g.Count() > 1))
                {
                    log.Warn($"Duplicate credential SID detected: {group.Key} ({group.Count()} entries)");
                }

                log.Verbosity = database.Settings.LogVerbosity;

                if (!credResult.PinBypassed && rememberPinService.IsEnabled)
                {
                    try
                    {
                        rememberPinService.UpdateForPinChange(sessionPinDerivedKey);
                    }
                    catch (Exception ex)
                    {
                        log.Warn($"Failed to refresh Remember PIN key after successful PIN entry; disabling feature: {ex.Message}");
                        try
                        {
                            rememberPinService.Disable();
                        }
                        catch (Exception cleanupEx)
                        {
                            log.Warn($"Failed to clean up Remember PIN key material after startup refresh error: {cleanupEx.Message}");
                        }
                    }
                }

                using var session = new SessionContext
                {
                    Database = database,
                    CredentialStore = credentialStore,
                    LastPinVerifiedAt = DateTime.UtcNow
                };
                session.ReplacePinDerivedKey(sessionPinDerivedKey);
                sessionPinDerivedKey = null;

                var options = new StartupOptions(isBackground, credResult.PinBypassed, grantStartupRunAsUnlock);
                using var sessionScope = sessionScopeFactory.BeginSessionScope(session, options);

                mainFormRunner.Run(sessionScope);
                return 0;
            }
            finally
            {
                mismatchKey?.Dispose();
                sessionPinDerivedKey?.Dispose();
            }
        }
    }

    private SelectedConfigLoadResult LoadSelectedConfigWithSessionKey(
        StartupConfigLoadSource source,
        ISecureSecretSnapshotSource sessionPinDerivedKey,
        byte[]? selectedConfigSalt)
    {
        if (!source.LoadFromPath)
        {
            var integrity = databaseService.VerifyConfigIntegrity(sessionPinDerivedKey);
            switch (integrity)
            {
                case ConfigIntegrityResult.FirstRun:
                    return new SelectedConfigLoadResult(
                        HasBackupRecoveryChoice(source)
                            ? ChooseRecoveryAction(source)
                            : StartupRecoveryAction.StartFresh);

                case ConfigIntegrityResult.Valid:
                    try
                    {
                        return new SelectedConfigLoadResult(
                            StartupRecoveryAction.Loaded,
                            LoadSelectedConfigWithSessionKeyCore(source, sessionPinDerivedKey));
                    }
                    catch (Exception ex) when (IsConfigKeyFailure(ex) && selectedConfigSalt != null)
                    {
                        if (TryPromptForSelectedConfigPin(source, selectedConfigSalt, out var database))
                            return new SelectedConfigLoadResult(
                                StartupRecoveryAction.Loaded,
                                database,
                                UsedMismatchRecovery: true);
                    }
                    catch (Exception ex)
                    {
                        log.Error($"Failed to load selected config from {source.SourcePath}", ex);
                    }

                    return new SelectedConfigLoadResult(ChooseRecoveryAction(source));

                case ConfigIntegrityResult.DecryptionFailed:
                default:
                    if (selectedConfigSalt != null &&
                        TryPromptForSelectedConfigPin(source, selectedConfigSalt, out var mismatchDatabase))
                    {
                        return new SelectedConfigLoadResult(
                            StartupRecoveryAction.Loaded,
                            mismatchDatabase,
                            UsedMismatchRecovery: true);
                    }

                    return new SelectedConfigLoadResult(ChooseRecoveryAction(source));
            }
        }

        try
        {
            return new SelectedConfigLoadResult(
                StartupRecoveryAction.Loaded,
                LoadSelectedConfigWithSessionKeyCore(source, sessionPinDerivedKey));
        }
        catch (Exception ex) when (IsConfigKeyFailure(ex) && selectedConfigSalt != null)
        {
            if (TryPromptForSelectedConfigPin(source, selectedConfigSalt, out var database))
            {
                return new SelectedConfigLoadResult(
                    StartupRecoveryAction.Loaded,
                    database,
                    UsedMismatchRecovery: true);
            }
        }
        catch (Exception ex)
        {
            log.Error($"Failed to load selected config from {source.SourcePath}", ex);
        }

        return new SelectedConfigLoadResult(ChooseRecoveryAction(source));
    }

    private AppDatabase LoadSelectedConfigWithSessionKeyCore(
        StartupConfigLoadSource source,
        ISecureSecretSnapshotSource sessionPinDerivedKey)
        => source.LoadFromPath
            ? databaseService.LoadConfigFromPath(source.SourcePath, sessionPinDerivedKey)
            : databaseService.LoadConfig(sessionPinDerivedKey);

    private AppDatabase LoadSelectedConfigWithMismatchKey(
        StartupConfigLoadSource source,
        ISecureSecretSnapshotSource mismatchKey)
        => source.LoadFromPath
            ? databaseService.LoadConfigFromPath(source.SourcePath, mismatchKey)
            : databaseService.LoadConfig(mismatchKey);

    private bool TryPromptForSelectedConfigPin(
        StartupConfigLoadSource source,
        byte[] selectedConfigSalt,
        out AppDatabase? database)
    {
        AppDatabase? verifiedDatabase = null;
        var promptResult = startupUi.PromptMainConfigMismatchPin(
            source.SourcePath,
            pin =>
            {
                verifiedDatabase = null;
                using var verification = configMismatchPinVerifier.VerifyTemporary(
                    pin,
                    selectedConfigSalt,
                    candidate => verifiedDatabase = LoadSelectedConfigWithMismatchKey(source, candidate));

                switch (verification.Status)
                {
                    case ConfigMismatchPinVerificationResult.StatusKind.VerifiedTemporaryOnly:
                        return MainConfigPinVerificationResult.Verified;

                    case ConfigMismatchPinVerificationResult.StatusKind.WrongPin:
                        return MainConfigPinVerificationResult.WrongPin;

                    case ConfigMismatchPinVerificationResult.StatusKind.AbortToRecovery:
                        if (verification.FatalException != null)
                            log.Error($"Failed to verify selected config PIN for {source.SourcePath}", verification.FatalException);
                        return MainConfigPinVerificationResult.AbortToRecovery;

                    default:
                        throw new InvalidOperationException($"Unexpected mismatch verification status: {verification.Status}");
                }
            });

        database = verifiedDatabase;
        return promptResult == MainConfigPinPromptResult.Verified && verifiedDatabase != null;
    }

    private AppDatabase CreateNewDatabase(CredentialStore credentialStore)
    {
        var database = new AppDatabase();
        appInit.InitializeNewDatabase(database);
        appInit.EnsureCurrentAccountCredential(credentialStore, database);
        return database;
    }

    private StartupRecoveryAction ChooseRecoveryAction(StartupConfigLoadSource source)
    {
        var backupAvailable = HasBackupRecoveryChoice(source);
        return startupUi.ConfirmStartFresh(backupAvailable) switch
        {
            StartupConfigRecoveryChoice.UseBackup when backupAvailable => StartupRecoveryAction.UseBackup,
            StartupConfigRecoveryChoice.Exit => StartupRecoveryAction.Exit,
            _ => StartupRecoveryAction.StartFresh
        };
    }

    private bool HasBackupRecoveryChoice(StartupConfigLoadSource source)
        => !source.LoadFromPath &&
           loadedGoodBackupStore.Exists(configPaths.ConfigFilePath);

    private void TryPreserveAcceptedStartupFile(string targetPath)
    {
        try
        {
            if (loadedGoodBackupStore.TryPreserveCurrentFile(targetPath, out var warning))
                return;

            log.Warn(warning ?? $"Could not preserve loaded-good backup for '{targetPath}'.");
        }
        catch (Exception ex)
        {
            log.Error($"Loaded-good backup preservation failed for '{targetPath}'", ex);
        }
    }

    private static bool IsConfigKeyFailure(Exception ex)
        => ex is CryptographicException;

    private enum StartupRecoveryAction
    {
        Loaded,
        StartFresh,
        UseBackup,
        Exit
    }

    private sealed record SelectedConfigLoadResult(
        StartupRecoveryAction Action,
        AppDatabase? Database = null,
        bool UsedMismatchRecovery = false);
}
