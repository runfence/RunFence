using System.Security.Cryptography;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;

namespace RunFence.Persistence.UI;

/// <summary>
/// Handles the full load and unload lifecycle for additional app config files:
/// loading with encryption/mismatch-key handling, enforcement application,
/// handler registration sync, post-commit warning reporting, and unload with revert.
/// Extracted from <see cref="ConfigManagementOrchestrator"/> together with
/// <see cref="IAppConfigService"/> and <see cref="ILoadedAppsCleanup"/> to reduce
/// <see cref="ConfigManagementOrchestrator"/>'s dependency count.
/// </summary>
public class ConfigLoadUnloadService(
    ISessionProvider sessionProvider,
    IAppConfigService appConfigService,
    ILoggingService log,
    ILicenseService licenseService,
    ILoadedGoodBackupStore loadedGoodBackupStore,
    ILoadedAppsCleanup enforcementHandler,
    ConfigMismatchKeyResolver mismatchKeyResolver,
    HandlerSyncHelper handlerSyncHelper,
    IHandlerMappingService handlerMappingService,
    IDatabaseService databaseService)
{
    /// <summary>
    /// Raised when one or more loaded apps had <c>ManageShortcuts</c> disabled due to ExePath
    /// conflicts with existing apps. Subscribe to show a custom conflict message; when no
    /// subscriber is present, the event is logged as a warning and ignored.
    /// </summary>
    public event Action<IReadOnlyList<string>>? ShortcutConflictsDetected;

    /// <summary>
    /// Raised when a config file could not be re-encrypted after load (e.g. read-only media).
    /// Subscribe to show a custom warning message; when no subscriber is present, the warning is silently suppressed.
    /// </summary>
    public event Action<string>? ReencryptionWarning;

    public IReadOnlyList<string> GetLoadedConfigPaths()
        => appConfigService.GetLoadedConfigPaths();

    public void SetGuardOwner(Control guardOwner)
        => mismatchKeyResolver.Initialize(guardOwner);

    public void RecomputeAllAncestorAcls(IReadOnlyList<AppEntry> allApps)
        => enforcementHandler.RecomputeAllAncestorAcls(allApps);

    public LoadAppsResult LoadApps(string configPath)
    {
        var session = sessionProvider.GetSession();
        var normalizedPath = Path.GetFullPath(configPath);
        SecureSecret? mismatchKey = null;
        try
        {
            mismatchKey = mismatchKeyResolver.TryDeriveConfigMismatchKey(normalizedPath);
            var staged = mismatchKey != null
                ? appConfigService.ReadAdditionalConfig(normalizedPath, session.Database, mismatchKey)
                : appConfigService.ReadAdditionalConfig(
                    normalizedPath,
                    session.Database,
                    session.PinDerivedKey);
            return CommitLoadedConfig(session, normalizedPath, staged, wasMismatch: mismatchKey != null);
        }
        catch (OperationCanceledException)
        {
            return new LoadAppsResult(false, "Cancelled.");
        }
        catch (Exception ex)
        {
            log.Error($"Failed to load config from {normalizedPath}", ex);
            bool backupAvailable;
            try
            {
                backupAvailable = loadedGoodBackupStore.Exists(normalizedPath);
            }
            catch
            {
                backupAvailable = false;
            }

            return new LoadAppsResult(
                false,
                ex.Message,
                BackupAvailable: backupAvailable);
        }
        finally
        {
            mismatchKey?.Dispose();
        }
    }

    public LoadAppsResult LoadAppConfigBackup(string configPath)
    {
        var session = sessionProvider.GetSession();
        var normalizedPath = Path.GetFullPath(configPath);
        var backupPath = loadedGoodBackupStore.GetBackupPath(normalizedPath);
        try
        {
            AppConfigMismatchLoadResult loadedBackup;
            var backupSalt = databaseService.TryGetAppConfigSaltFromPath(backupPath);
            if (backupSalt != null && !backupSalt.SequenceEqual(session.CredentialStore.ArgonSalt))
            {
                loadedBackup = mismatchKeyResolver.TryLoadAppConfigWithMismatchKey(backupPath)
                               ?? throw new CryptographicException(
                                   "Cannot decrypt config file. It may be encrypted with a different PIN.");
            }
            else
            {
                try
                {
                    var config = databaseService.LoadAppConfigFromPath(
                        backupPath,
                        session.PinDerivedKey);
                    loadedBackup = new AppConfigMismatchLoadResult(config, UsedMismatchKey: false);
                }
                catch (Exception ex) when (backupSalt != null && ex is CryptographicException)
                {
                    var mismatchResult = mismatchKeyResolver.TryLoadAppConfigWithMismatchKey(
                        backupPath,
                        forcePrompt: true);
                    if (mismatchResult != null)
                    {
                        loadedBackup = mismatchResult;
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            var staged = appConfigService.ReadAdditionalConfigFromBackup(
                normalizedPath,
                loadedBackup.Config,
                session.Database);
            return CommitLoadedConfig(session, normalizedPath, staged, loadedBackup.UsedMismatchKey);
        }
        catch (OperationCanceledException)
        {
            return new LoadAppsResult(false, "Cancelled.");
        }
        catch (Exception ex)
        {
            log.Error($"Failed to load config backup from {backupPath}", ex);
            return new LoadAppsResult(false, ex.Message);
        }
    }

    private LoadAppsResult CommitLoadedConfig(
        SessionContext session,
        string configPath,
        AdditionalConfigLoadData staged,
        bool wasMismatch)
    {
        List<AppEntry>? loadedApps = null;
        var commitAttempted = false;
        var committed = false;
        var warnings = new List<string>();
        try
        {
            loadedApps = staged.Apps;
            if (staged.SkipCommit)
                return new LoadAppsResult(true, null);

            if (loadedApps.Count > 0)
            {
                var restrictionMsg = licenseService.GetRestrictionMessage(
                    EvaluationFeature.Apps, session.Database.Apps.Count + loadedApps.Count - 1);
                if (restrictionMsg != null)
                    throw new InvalidOperationException($"Cannot load config: {restrictionMsg}");
            }

            var conflicts = ResolveShortcutConflicts(session.Database, loadedApps);

            commitAttempted = true;
            loadedApps = appConfigService.ApplyAdditionalConfig(staged, session.Database);
            committed = true;

            if (!SaveLoadedConfig(session, configPath, wasMismatch, warnings))
            {
                RaiseShortcutConflictWarnings(conflicts);
                return new LoadAppsResult(true, null, warnings.Count == 0 ? null : warnings);
            }

            try
            {
                if (!loadedGoodBackupStore.TryPreserveCurrentFile(configPath, out var backupWarning))
                    log.Warn(backupWarning ?? $"Could not preserve loaded-good backup for '{configPath}'.");
            }
            catch (Exception ex)
            {
                log.Error($"Loaded config backup preservation failed for {configPath}", ex);
            }

            try
            {
                enforcementHandler.ApplyLoadedAppsEnforcement(loadedApps);
            }
            catch (Exception ex)
            {
                log.Error($"Loaded config enforcement failed for {configPath}", ex);
                warnings.Add($"Loaded config, but enforcement failed: {ex.Message}");
            }

            try
            {
                handlerSyncHelper.Sync();
            }
            catch (Exception ex)
            {
                log.Error($"Loaded config handler sync failed for {configPath}", ex);
                warnings.Add($"Loaded config, but handler sync failed: {ex.Message}");
            }

            RaiseShortcutConflictWarnings(conflicts);
            return new LoadAppsResult(true, null, warnings.Count == 0 ? null : warnings);
        }
        catch (OperationCanceledException)
        {
            if (commitAttempted && !committed)
                RollbackFailedLoad(configPath);
            return new LoadAppsResult(false, "Cancelled.");
        }
        catch (Exception ex)
        {
            if (commitAttempted && !committed)
                RollbackFailedLoad(configPath);
            if (committed)
            {
                log.Error($"Loaded config post-commit step failed for {configPath}", ex);
                warnings.Add($"Loaded config, but a post-load step failed: {ex.Message}");
                return new LoadAppsResult(true, null, warnings.Count == 0 ? null : warnings);
            }

            throw;
        }
    }

    private List<string> ResolveShortcutConflicts(AppDatabase database, List<AppEntry> loadedApps)
    {
        var conflicts = new List<string>();
        for (var i = 0; i < loadedApps.Count; i++)
        {
            var app = loadedApps[i];
            if (!app.ManageShortcuts)
                continue;

            var existing = database.Apps
                .Concat(loadedApps.Take(i))
                .FirstOrDefault(a =>
                    a != app &&
                    a.ManageShortcuts &&
                    string.Equals(a.ExePath, app.ExePath, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
                continue;

            app.ManageShortcuts = false;
            conflicts.Add(app.Name);
            log.Warn($"Disabled ManageShortcuts on '{app.Name}' due to ExePath conflict with '{existing.Name}'");
        }

        return conflicts;
    }

    private bool SaveLoadedConfig(
        SessionContext session,
        string configPath,
        bool wasMismatch,
        List<string> warnings)
    {
        try
        {
            appConfigService.SaveConfigAtPath(
                configPath,
                session.Database,
                session.PinDerivedKey,
                session.CredentialStore.ArgonSalt);
            return true;
        }
        catch (Exception ex) when (IsExpectedPostLoadSaveWarning(ex))
        {
            log.Warn($"Could not save config after load: {ex.Message}");
            warnings.Add($"Loaded config, but saving it failed: {ex.Message}");
            RaiseReencryptionWarning(wasMismatch, readOnlyOrAclDenied: true);
            return false;
        }
        catch (Exception ex)
        {
            log.Error($"Could not save config after load: {ex.Message}", ex);
            warnings.Add($"Loaded config, but saving it failed: {ex.Message}");
            RaiseReencryptionWarning(wasMismatch, readOnlyOrAclDenied: false);
            return false;
        }
    }

    private static bool IsExpectedPostLoadSaveWarning(Exception ex) =>
        ex is IOException or UnauthorizedAccessException;

    private void RaiseShortcutConflictWarnings(IReadOnlyList<string> conflicts)
    {
        if (conflicts.Count == 0)
            return;

        if (ShortcutConflictsDetected != null)
            ShortcutConflictsDetected(conflicts);
        else
            log.Warn("ShortcutConflictsDetected has no subscribers - conflicts ignored");
    }

    private void RaiseReencryptionWarning(bool wasMismatch, bool readOnlyOrAclDenied)
    {
        if (!wasMismatch)
            return;

        var warning = readOnlyOrAclDenied
            ? "The config file could not be re-encrypted with your current PIN " +
              "(possibly on read-only media). You will be prompted for the old PIN next time."
            : "The config file could not be re-encrypted with your current PIN after load. " +
              "You will be prompted for the old PIN next time.";
        ReencryptionWarning?.Invoke(warning);
    }

    private void RollbackFailedLoad(string configPath)
    {
        var session = sessionProvider.GetSession();
        try
        {
            SyncRemovedHandlerKeys(session.Database, () =>
            {
                UnloadAndRevertConfig(configPath, session.Database);
                enforcementHandler.RecomputeAllAncestorAcls(session.Database.Apps);
            });
            log.Info($"Rolled back failed load of config: {configPath}");
        }
        catch (Exception ex)
        {
            log.Error($"Rollback failed for config: {configPath}", ex);
        }
    }

    public void UnloadAndRevertConfig(string configPath, AppDatabase database)
    {
        var removed = appConfigService.UnloadConfig(configPath, database);
        enforcementHandler.RevertApps(removed);
    }

    public void SyncRemovedHandlerKeys(AppDatabase database, Action unloadAction)
    {
        var previousKeys = handlerMappingService.GetEffectiveHandlerMappings(database).Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        unloadAction();

        var removedKeys = previousKeys
            .Except(handlerMappingService.GetEffectiveHandlerMappings(database).Keys,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        handlerSyncHelper.Sync(removedKeys);
    }
}
