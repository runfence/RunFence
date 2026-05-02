using System.Security.Cryptography;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;

namespace RunFence.Persistence.UI;

/// <summary>
/// Handles the full load and unload lifecycle for additional app config files:
/// loading with encryption/mismatch-key handling, enforcement application,
/// handler registration sync, rollback on failure, and unload with revert.
/// Extracted from <see cref="ConfigManagementOrchestrator"/> together with
/// <see cref="IAppConfigService"/> and <see cref="ILoadedAppsCleanup"/> to reduce
/// <see cref="ConfigManagementOrchestrator"/>'s dependency count.
/// </summary>
public class ConfigLoadUnloadService(
    ISessionProvider sessionProvider,
    IAppConfigService appConfigService,
    ILoggingService log,
    ILicenseService licenseService,
    ILoadedAppsCleanup enforcementHandler,
    ConfigMismatchKeyResolver mismatchKeyResolver,
    HandlerSyncHelper handlerSyncHelper,
    IHandlerMappingService handlerMappingService)
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

    public (bool success, string? errorMessage) LoadApps(string configPath)
    {
        var session = sessionProvider.GetSession();
        byte[]? mismatchKey = null;
        List<AppEntry>? loadedApps = null;
        try
        {
            mismatchKey = mismatchKeyResolver.TryDeriveConfigMismatchKey(configPath);

            using var scope = session.PinDerivedKey.Unprotect();

            bool wasMismatch = mismatchKey != null;
            try
            {
                loadedApps = appConfigService.LoadAdditionalConfig(
                    configPath, session.Database, mismatchKey ?? scope.Data);
            }
            finally
            {
                if (mismatchKey != null)
                {
                    CryptographicOperations.ZeroMemory(mismatchKey);
                    mismatchKey = null;
                }
            }

            if (loadedApps.Count > 0)
            {
                // Pass (totalCount - 1) so that exactly EvaluationMaxApps apps is allowed;
                // only exceeding it triggers the restriction message.
                var restrictionMsg = licenseService.GetRestrictionMessage(
                    EvaluationFeature.Apps, session.Database.Apps.Count - 1);
                if (restrictionMsg != null)
                    throw new InvalidOperationException(
                        $"Cannot load config: {restrictionMsg}");
            }

            var conflicts = new List<string>();
            foreach (var app in loadedApps)
            {
                if (app.ManageShortcuts)
                {
                    var existing = session.Database.Apps.FirstOrDefault(a =>
                        a != app &&
                        a.ManageShortcuts &&
                        string.Equals(a.ExePath, app.ExePath, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        app.ManageShortcuts = false;
                        conflicts.Add(app.Name);
                        log.Warn($"Disabled ManageShortcuts on '{app.Name}' due to ExePath conflict with '{existing.Name}'");
                    }
                }
            }

            enforcementHandler.ApplyLoadedAppsEnforcement(loadedApps);

            // Sync handler registrations — loaded config's associations are now in the effective set
            handlerSyncHelper.Sync();

            try
            {
                appConfigService.SaveConfigAtPath(configPath, session.Database,
                    scope.Data, session.CredentialStore.ArgonSalt);
            }
            catch (IOException ex)
            {
                log.Warn($"Could not save config after load: {ex.Message}");
                if (wasMismatch)
                    ReencryptionWarning?.Invoke(
                        "The config file could not be re-encrypted with your current PIN " +
                        "(possibly on read-only media). You will be prompted for the old PIN next time.");
            }

            if (conflicts.Count > 0)
            {
                if (ShortcutConflictsDetected != null)
                    ShortcutConflictsDetected(conflicts);
                else
                    log.Warn("ShortcutConflictsDetected has no subscribers — conflicts ignored");
            }

            return (true, null);
        }
        catch (OperationCanceledException)
        {
            if (mismatchKey != null)
            {
                CryptographicOperations.ZeroMemory(mismatchKey);
                mismatchKey = null;
            }

            if (loadedApps != null)
                RollbackFailedLoad(configPath);
            return (false, "Cancelled.");
        }
        catch (Exception ex)
        {
            if (mismatchKey != null)
            {
                CryptographicOperations.ZeroMemory(mismatchKey);
                mismatchKey = null;
            }

            if (loadedApps != null)
                RollbackFailedLoad(configPath);
            log.Error($"Failed to load config from {configPath}", ex);
            return (false, ex.Message);
        }
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
