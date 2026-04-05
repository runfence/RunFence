using System.Security.Cryptography;
using RunFence.Acl.QuickAccess;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;

namespace RunFence.Persistence.UI;

public class ConfigManagementOrchestrator : IConfigManagementContext, IDisposable
{
    private readonly ISessionProvider _sessionProvider;
    private readonly IAppConfigService _appConfigService;
    private readonly IConfigRepository _configRepository;
    private readonly ILoggingService _log;
    private readonly ILicenseService _licenseService;
    private readonly IQuickAccessPinService _quickAccessPinService;
    private readonly Action<IReadOnlyList<string>>? _onShortcutConflicts;
    private readonly ConfigAvailabilityMonitor? _availabilityMonitor;
    private readonly ConfigEnforcementOrchestrator _enforcementHandler;
    private readonly ConfigMismatchKeyResolver _mismatchKeyResolver;
    private readonly HandlerSyncHelper _handlerSyncHelper;

    public event Action? DataRefreshRequested;
    public event Action? TrayUpdateRequested;

    public ConfigManagementOrchestrator(
        ISessionProvider sessionProvider,
        IAppConfigService appConfigService,
        IConfigRepository configRepository,
        ILoggingService log,
        ConfigEnforcementOrchestrator enforcementHandler,
        ConfigMismatchKeyResolver mismatchKeyResolver,
        HandlerSyncHelper handlerSyncHelper,
        ILicenseService licenseService,
        IQuickAccessPinService quickAccessPinService,
        ApplicationState? applicationState = null,
        IUiThreadInvoker? uiThreadInvoker = null,
        Action<IReadOnlyList<string>>? onShortcutConflicts = null)
    {
        _sessionProvider = sessionProvider;
        _appConfigService = appConfigService;
        _configRepository = configRepository;
        _log = log;
        _licenseService = licenseService;
        _quickAccessPinService = quickAccessPinService;
        _onShortcutConflicts = onShortcutConflicts;
        _enforcementHandler = enforcementHandler;
        _mismatchKeyResolver = mismatchKeyResolver;
        _handlerSyncHelper = handlerSyncHelper;
        if (applicationState != null && uiThreadInvoker != null)
            _availabilityMonitor = new ConfigAvailabilityMonitor(
                appConfigService, log, AutoUnloadUnavailableConfigs,
                applicationState, uiThreadInvoker, applicationState.EnforcementGuard);
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
                RollbackFailedLoad(configPath, loadedApps);
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
                RollbackFailedLoad(configPath, loadedApps);
            _log.Error($"Failed to load config from {configPath}", ex);
            return (false, ex.Message);
        }
    }

    private void RollbackFailedLoad(string configPath, IReadOnlyList<AppEntry> loadedApps)
    {
        var session = _sessionProvider.GetSession();
        try
        {
            // Mirror UnloadApps order: remove from database first so RevertApps sees correct remainingApps
            _appConfigService.UnloadConfig(configPath, session.Database);
            _enforcementHandler.RevertApps(loadedApps);
            _enforcementHandler.RecomputeAllAncestorAcls(session.Database.Apps);
            _handlerSyncHelper.Sync();
            _log.Info($"Rolled back failed load of config: {configPath}");
        }
        catch (Exception ex)
        {
            _log.Error($"Rollback failed for config: {configPath}", ex);
        }
    }

    public IReadOnlyList<string> GetLoadedConfigPaths()
        => _appConfigService.GetLoadedConfigPaths();

    public bool UnloadApps(string configPath)
    {
        var session = _sessionProvider.GetSession();
        try
        {
            var snapshotBefore = SnapshotAllowGrantPaths(session.Database);

            var removed = _appConfigService.UnloadConfig(configPath, session.Database);

            _enforcementHandler.RevertApps(removed);

            _enforcementHandler.RecomputeAllAncestorAcls(session.Database.Apps);

            // Sync handler registrations — unloaded config's associations are gone from the effective set
            _handlerSyncHelper.Sync();

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

    public void SaveSecurityFindingsHash()
    {
        var session = _sessionProvider.GetSession();
        using var scope = session.PinDerivedKey.Unprotect();
        _configRepository.SaveConfig(session.Database, scope.Data, session.CredentialStore.ArgonSalt);
    }

    public void SaveConfigAfterEnforcement(AppDatabase database)
    {
        var session = _sessionProvider.GetSession();
        using var scope = session.PinDerivedKey.Unprotect();
        _appConfigService.SaveAllConfigs(database, scope.Data, session.CredentialStore.ArgonSalt);
    }

    public void CleanupAllApps(bool isEnforcementInProgress, bool isOperationInProgress)
    {
        var result = _enforcementHandler.CleanupAllApps(isEnforcementInProgress, isOperationInProgress);
        if (result == CleanupAllAppsResult.OperationInProgress)
        {
            MessageBox.Show(
                "An operation is in progress. Please wait for it to complete before cleaning up.",
                "Operation In Progress",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        Application.Exit();
    }

    /// <summary>
    /// Sets the guard owner control used to disable UI during enforcement operations.
    /// Call after the host form is created to complete wiring.
    /// </summary>
    public void SetGuardOwner(Control guardOwner)
    {
        _mismatchKeyResolver.SetGuardOwner(guardOwner);
    }

    public void ScheduleAvailabilityCheck()
        => _availabilityMonitor?.ScheduleAvailabilityCheck();

    private void AutoUnloadUnavailableConfigs(List<string> unavailable)
    {
        var session = _sessionProvider.GetSession();
        try
        {
            var snapshotBefore = SnapshotAllowGrantPaths(session.Database);

            foreach (var path in unavailable)
            {
                var removed = _appConfigService.UnloadConfig(path, session.Database);
                _enforcementHandler.RevertApps(removed);
                _log.Info($"Auto-unloaded config: {path}");
            }

            UnpinRemovedGrantPaths(session.Database, snapshotBefore);

            _enforcementHandler.RecomputeAllAncestorAcls(session.Database.Apps);
            _handlerSyncHelper.Sync();
        }
        catch (Exception ex)
        {
            _log.Error("Availability check auto-unload failed", ex);
        }

        DataRefreshRequested?.Invoke();
        TrayUpdateRequested?.Invoke();
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