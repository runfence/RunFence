using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Persistence.UI;

public class ConfigManagementOrchestrator : IConfigManagementContext, IAdditionalConfigLoadService, IConfigAvailabilityChecker, IConfigManagementEventSource, IDisposable
{
    private readonly ISessionProvider _sessionProvider;
    private readonly ILoggingService _log;
    private readonly ConfigLoadUnloadService _configLoadUnloadService;
    private readonly ConfigGrantPinHelper _configGrantPinHelper;
    private readonly ShutdownCleanupService _shutdownCleanupService;
    private readonly ConfigAvailabilityMonitor? _availabilityMonitor;

    public event Action? DataRefreshRequested;
    public event Action? TrayUpdateRequested;

    /// <summary>
    /// Raised when one or more loaded apps had <c>ManageShortcuts</c> disabled due to ExePath
    /// conflicts with existing apps. Subscribe to show a custom conflict message; when no
    /// subscriber is present, the event is logged as a warning and ignored.
    /// </summary>
    public event Action<IReadOnlyList<string>>? ShortcutConflictsDetected
    {
        add => _configLoadUnloadService.ShortcutConflictsDetected += value;
        remove => _configLoadUnloadService.ShortcutConflictsDetected -= value;
    }

    /// <summary>
    /// Raised when a config file could not be re-encrypted after load (e.g. read-only media).
    /// Callers should subscribe to show a warning message to the user.
    /// </summary>
    public event Action<string>? ReencryptionWarning
    {
        add => _configLoadUnloadService.ReencryptionWarning += value;
        remove => _configLoadUnloadService.ReencryptionWarning -= value;
    }

    public ConfigManagementOrchestrator(
        ISessionProvider sessionProvider,
        ILoggingService log,
        ConfigLoadUnloadService configLoadUnloadService,
        ConfigGrantPinHelper configGrantPinHelper,
        ShutdownCleanupService shutdownCleanupService,
        ConfigAvailabilityMonitor? availabilityMonitor = null)
    {
        _sessionProvider = sessionProvider;
        _log = log;
        _configLoadUnloadService = configLoadUnloadService;
        _configGrantPinHelper = configGrantPinHelper;
        _shutdownCleanupService = shutdownCleanupService;
        _availabilityMonitor = availabilityMonitor;
        if (_availabilityMonitor != null)
            _availabilityMonitor.AutoUnloadRequired += (_, unavailable) => AutoUnloadUnavailableConfigs(unavailable);
    }

    public LoadAppsResult LoadApps(string configPath)
    {
        var result = _configLoadUnloadService.LoadApps(configPath);
        if (result.Succeeded)
        {
            DataRefreshRequested?.Invoke();
            TrayUpdateRequested?.Invoke();
            _configGrantPinHelper.PinAllGrantedFolders();
        }
        return result;
    }

    public IReadOnlyList<string> GetLoadedConfigPaths()
        => _configLoadUnloadService.GetLoadedConfigPaths();

    public LoadAppsResult LoadAppConfigBackup(string configPath)
    {
        var result = _configLoadUnloadService.LoadAppConfigBackup(configPath);
        if (result.Succeeded)
        {
            DataRefreshRequested?.Invoke();
            TrayUpdateRequested?.Invoke();
            _configGrantPinHelper.PinAllGrantedFolders();
        }

        return result;
    }
    public bool UnloadApps(string configPath)
    {
        var session = _sessionProvider.GetSession();
        try
        {
            var snapshotBefore = ConfigGrantPinHelper.SnapshotAllowGrantPaths(session.Database);

            _configLoadUnloadService.SyncRemovedHandlerKeys(session.Database, () =>
            {
                _configLoadUnloadService.UnloadAndRevertConfig(configPath, session.Database);
                _configLoadUnloadService.RecomputeAllAncestorAcls(session.Database.Apps);
            });

            DataRefreshRequested?.Invoke();
            TrayUpdateRequested?.Invoke();

            _configGrantPinHelper.UnpinRemovedGrantPaths(session.Database, snapshotBefore);

            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to unload config from {configPath}", ex);
            return false;
        }
    }

    public CleanupAllAppsResult CleanupAllApps(bool isEnforcementInProgress, bool isOperationInProgress)
        => _shutdownCleanupService.CleanupAllApps(isEnforcementInProgress, isOperationInProgress);

    /// <summary>
    /// Sets the guard owner control used to disable UI during enforcement operations.
    /// Call after the host form is created to complete wiring.
    /// </summary>
    public void SetGuardOwner(Control guardOwner)
        => _configLoadUnloadService.SetGuardOwner(guardOwner);

    public void ScheduleAvailabilityCheck()
        => _availabilityMonitor?.ScheduleAvailabilityCheck();

    private void AutoUnloadUnavailableConfigs(List<string> unavailable)
    {
        var session = _sessionProvider.GetSession();
        try
        {
            var snapshotBefore = ConfigGrantPinHelper.SnapshotAllowGrantPaths(session.Database);

            _configLoadUnloadService.SyncRemovedHandlerKeys(session.Database, () =>
            {
                foreach (var path in unavailable)
                {
                    _configLoadUnloadService.UnloadAndRevertConfig(path, session.Database);
                    _log.Info($"Auto-unloaded config: {path}");
                }

                _configGrantPinHelper.UnpinRemovedGrantPaths(session.Database, snapshotBefore);
                _configLoadUnloadService.RecomputeAllAncestorAcls(session.Database.Apps);
            });
        }
        catch (Exception ex)
        {
            _log.Error("Availability check auto-unload failed", ex);
        }

        DataRefreshRequested?.Invoke();
        TrayUpdateRequested?.Invoke();
    }

    public void Dispose() => _availabilityMonitor?.Dispose();
}
