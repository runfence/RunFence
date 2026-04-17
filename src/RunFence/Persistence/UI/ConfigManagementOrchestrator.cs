using System.Security.Cryptography;
using RunFence.Acl.QuickAccess;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;

namespace RunFence.Persistence.UI;

/// <remarks>Deps above threshold: Load and Unload flows share 7 of 9 non-optional deps (<c>_sessionProvider</c>, <c>_appConfigService</c>, <c>_enforcementHandler</c>, <c>_handlerSyncHelper</c>, <c>_quickAccessPinService</c>, <c>_log</c>, <c>_mismatchKeyResolver</c>). Splitting load/unload into separate classes duplicates these deps — strictly worse than 11 in one place. Reviewed 2026-04-08.</remarks>
public class ConfigManagementOrchestrator : IConfigManagementContext, IDisposable
{
    private readonly ISessionProvider _sessionProvider;
    private readonly IAppConfigService _appConfigService;
    private readonly ILoggingService _log;
    private readonly ILicenseService _licenseService;
    private readonly IQuickAccessPinService _quickAccessPinService;
    private readonly Action<IReadOnlyList<string>>? _onShortcutConflicts;
    private readonly ConfigAvailabilityMonitor? _availabilityMonitor;
    private readonly ConfigEnforcementOrchestrator _enforcementHandler;
    private readonly ConfigMismatchKeyResolver _mismatchKeyResolver;
    private readonly HandlerSyncHelper _handlerSyncHelper;
    private readonly IHandlerMappingService _handlerMappingService;

    public event Action? DataRefreshRequested;
    public event Action? TrayUpdateRequested;

    public ConfigManagementOrchestrator(
        ISessionProvider sessionProvider,
        IAppConfigService appConfigService,
        ILoggingService log,
        ConfigEnforcementOrchestrator enforcementHandler,
        ConfigMismatchKeyResolver mismatchKeyResolver,
        HandlerSyncHelper handlerSyncHelper,
        IHandlerMappingService handlerMappingService,
        ILicenseService licenseService,
        IQuickAccessPinService quickAccessPinService,
        ConfigAvailabilityMonitor? availabilityMonitor = null,
        Action<IReadOnlyList<string>>? onShortcutConflicts = null)
    {
        _sessionProvider = sessionProvider;
        _appConfigService = appConfigService;
        _log = log;
        _licenseService = licenseService;
        _quickAccessPinService = quickAccessPinService;
        _onShortcutConflicts = onShortcutConflicts;
        _enforcementHandler = enforcementHandler;
        _mismatchKeyResolver = mismatchKeyResolver;
        _handlerSyncHelper = handlerSyncHelper;
        _handlerMappingService = handlerMappingService;
        _availabilityMonitor = availabilityMonitor;
        if (_availabilityMonitor != null)
            _availabilityMonitor.AutoUnloadRequired += (_, unavailable) => AutoUnloadUnavailableConfigs(unavailable);
    }

    public (bool success, string? errorMessage) LoadApps(string configPath)
    {
        var result = LoadAppsCore(configPath);
        if (result.success)
            _quickAccessPinService.PinAllGrantedFolders();
        return result;
    }

    private (bool success, string? errorMessage) LoadAppsCore(string configPath)
    {
        var session = _sessionProvider.GetSession();
        byte[]? mismatchKey = null;
        List<AppEntry>? loadedApps = null;
        try
        {
            mismatchKey = _mismatchKeyResolver.TryDeriveConfigMismatchKey(configPath);

            using var scope = session.PinDerivedKey.Unprotect();

            bool wasMismatch = mismatchKey != null;
            try
            {
                loadedApps = _appConfigService.LoadAdditionalConfig(
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
                var restrictionMsg = _licenseService.GetRestrictionMessage(
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
                        _log.Warn($"Disabled ManageShortcuts on '{app.Name}' due to ExePath conflict with '{existing.Name}'");
                    }
                }
            }

            _enforcementHandler.ApplyLoadedAppsEnforcement(loadedApps);

            // Sync handler registrations — loaded config's associations are now in the effective set
            _handlerSyncHelper.Sync();

            try
            {
                _appConfigService.SaveConfigAtPath(configPath, session.Database,
                    scope.Data, session.CredentialStore.ArgonSalt);
            }
            catch (IOException ex)
            {
                _log.Warn($"Could not save config after load: {ex.Message}");
                if (wasMismatch)
                    MessageBox.Show(
                        "The config file could not be re-encrypted with your current PIN " +
                        "(possibly on read-only media). You will be prompted for the old PIN next time.",
                        "Re-encryption Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            DataRefreshRequested?.Invoke();
            TrayUpdateRequested?.Invoke();

            if (conflicts.Count > 0)
            {
                if (_onShortcutConflicts != null)
                    _onShortcutConflicts(conflicts);
                else
                {
                    var names = string.Join(", ", conflicts.Select(n => $"'{n}'"));
                    MessageBox.Show(
                        $"ManageShortcuts was disabled for {names} due to ExePath conflicts with existing apps.",
                        "Shortcut Conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
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
            _log.Error($"Failed to load config from {configPath}", ex);
            return (false, ex.Message);
        }
    }

    private void RollbackFailedLoad(string configPath)
    {
        var session = _sessionProvider.GetSession();
        try
        {
            SyncRemovedHandlerKeys(session.Database, () =>
            {
                UnloadAndRevertConfig(configPath, session.Database);
                _enforcementHandler.RecomputeAllAncestorAcls(session.Database.Apps);
            });
            _log.Info($"Rolled back failed load of config: {configPath}");
        }
        catch (Exception ex)
        {
            _log.Error($"Rollback failed for config: {configPath}", ex);
        }
    }

    private void UnloadAndRevertConfig(string configPath, AppDatabase database)
    {
        var removed = _appConfigService.UnloadConfig(configPath, database);
        _enforcementHandler.RevertApps(removed);
    }

    public IReadOnlyList<string> GetLoadedConfigPaths()
        => _appConfigService.GetLoadedConfigPaths();

    public bool UnloadApps(string configPath)
    {
        var session = _sessionProvider.GetSession();
        try
        {
            var snapshotBefore = SnapshotAllowGrantPaths(session.Database);

            SyncRemovedHandlerKeys(session.Database, () =>
            {
                UnloadAndRevertConfig(configPath, session.Database);
                _enforcementHandler.RecomputeAllAncestorAcls(session.Database.Apps);
            });

            DataRefreshRequested?.Invoke();
            TrayUpdateRequested?.Invoke();

            UnpinRemovedGrantPaths(session.Database, snapshotBefore);

            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to unload config from {configPath}", ex);
            return false;
        }
    }

    public CleanupAllAppsResult CleanupAllApps(bool isEnforcementInProgress, bool isOperationInProgress)
        => _enforcementHandler.CleanupAllApps(isEnforcementInProgress, isOperationInProgress);

    /// <summary>
    /// Sets the guard owner control used to disable UI during enforcement operations.
    /// Call after the host form is created to complete wiring.
    /// </summary>
    public void SetGuardOwner(Control guardOwner)
    {
        _mismatchKeyResolver.Initialize(guardOwner);
    }

    public void ScheduleAvailabilityCheck()
        => _availabilityMonitor?.ScheduleAvailabilityCheck();

    private void AutoUnloadUnavailableConfigs(List<string> unavailable)
    {
        var session = _sessionProvider.GetSession();
        try
        {
            var snapshotBefore = SnapshotAllowGrantPaths(session.Database);

            SyncRemovedHandlerKeys(session.Database, () =>
            {
                foreach (var path in unavailable)
                {
                    UnloadAndRevertConfig(path, session.Database);
                    _log.Info($"Auto-unloaded config: {path}");
                }

                UnpinRemovedGrantPaths(session.Database, snapshotBefore);
                _enforcementHandler.RecomputeAllAncestorAcls(session.Database.Apps);
            });
        }
        catch (Exception ex)
        {
            _log.Error("Availability check auto-unload failed", ex);
        }

        DataRefreshRequested?.Invoke();
        TrayUpdateRequested?.Invoke();
    }

    private void SyncRemovedHandlerKeys(AppDatabase database, Action unloadAction)
    {
        var previousKeys = _handlerMappingService.GetEffectiveHandlerMappings(database).Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        unloadAction();

        var removedKeys = previousKeys
            .Except(_handlerMappingService.GetEffectiveHandlerMappings(database).Keys,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        _handlerSyncHelper.Sync(removedKeys);
    }

    private static Dictionary<string, HashSet<string>> SnapshotAllowGrantPaths(AppDatabase database)
        => database.Accounts.ToDictionary(
            a => a.Sid,
            a => a.Grants
                .Where(g => !g.IsTraverseOnly && !g.IsDeny)
                .Select(g => g.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));

    private void UnpinRemovedGrantPaths(AppDatabase database, Dictionary<string, HashSet<string>> snapshotBefore)
    {
        foreach (var (sid, pathsBefore) in snapshotBefore)
        {
            var currentPaths = database.GetAccount(sid)?.Grants
                .Where(g => !g.IsTraverseOnly && !g.IsDeny)
                .Select(g => g.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            var unpinPaths = pathsBefore.Except(currentPaths, StringComparer.OrdinalIgnoreCase).ToList();
            if (unpinPaths.Count > 0)
                _quickAccessPinService.UnpinFolders(sid, unpinPaths);
        }
    }

    public void Dispose() => _availabilityMonitor?.Dispose();
}