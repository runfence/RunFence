using RunFence.Core;
using RunFence.Infrastructure;
using Timer = System.Windows.Forms.Timer;

namespace RunFence.Persistence.UI;

/// <summary>
/// Monitors loaded additional config file paths for availability (e.g., removable media removed).
/// Automatically unloads configs that are no longer accessible, then calls
/// <param cref="onAutoUnload"/> to revert enforcement and refresh the UI.
/// </summary>
public class ConfigAvailabilityMonitor(
    IAppConfigService appConfigService,
    ILoggingService log,
    Action<List<string>> onAutoUnload,
    IAppStateProvider appStateProvider,
    IUiThreadInvoker uiThreadInvoker,
    OperationGuard enforcementGuard)
    : IDisposable
{
    private Timer? _timer;

    /// <summary>
    /// Arms (or re-arms) the availability check timer. Safe to call from the UI thread at any time.
    /// Has no effect if the app is shutting down, an enforcement/modal is in progress, or no configs are loaded.
    /// </summary>
    public void ScheduleAvailabilityCheck()
    {
        if (appStateProvider.IsShuttingDown)
            return;
        if (enforcementGuard.IsInProgress)
            return;
        if (appStateProvider.IsModalOpen)
            return;
        if (!appConfigService.HasLoadedConfigs)
            return;

        if (_timer == null)
        {
            _timer = new Timer { Interval = 1000 };
            _timer.Tick += (_, _) => OnAvailabilityCheckTick();
        }

        _timer.Stop();
        _timer.Start();
    }

    private void OnAvailabilityCheckTick()
    {
        _timer?.Stop();
        if (appStateProvider.IsShuttingDown)
            return;

        if (enforcementGuard.IsInProgress)
            return;
        if (appStateProvider.IsModalOpen)
            return;

        var pathsToCheck = appConfigService.GetLoadedConfigPaths().ToList();
        if (pathsToCheck.Count == 0)
            return;

        _ = Task.Run(() =>
        {
            var unavailable = pathsToCheck.Where(p => !File.Exists(p)).ToList();
            if (unavailable.Count == 0 || appStateProvider.IsShuttingDown)
                return;
            uiThreadInvoker.BeginInvoke(() => AutoUnloadUnavailableConfigs(unavailable));
        });
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void AutoUnloadUnavailableConfigs(List<string> unavailable)
    {
        if (appStateProvider.IsShuttingDown)
            return;

        if (enforcementGuard.IsInProgress)
            return;
        if (appStateProvider.IsModalOpen)
            return;

        // Re-check availability on the UI thread — the file may have reappeared
        // between the background check and the UI thread dispatch.
        unavailable = unavailable.Where(p => !File.Exists(p)).ToList();
        if (unavailable.Count == 0)
            return;

        log.Info($"Auto-unloading {unavailable.Count} unavailable config path(s)");
        onAutoUnload(unavailable);
    }
}