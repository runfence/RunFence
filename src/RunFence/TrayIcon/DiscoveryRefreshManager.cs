using RunFence.Core;
using RunFence.Core.Models;
using Timer = System.Windows.Forms.Timer;

namespace RunFence.TrayIcon;

/// <summary>
/// Manages start-menu discovery scanning and updates the tray with discovered apps.
/// Supports both debounced (scheduled) and immediate refresh.
/// </summary>
public sealed class DiscoveryRefreshManager : IDisposable
{
    private readonly StartMenuDiscoveryService _discoveryService;
    private readonly SessionContext _session;
    private readonly ILoggingService _log;
    private TrayIconManager? _trayManager;
    private Control? _host;
    private Timer? _debounceTimer;
    private int _generation;

    public DiscoveryRefreshManager(StartMenuDiscoveryService discoveryService, SessionContext session, ILoggingService log)
    {
        _discoveryService = discoveryService;
        _session = session;
        _log = log;
    }

    public void SetHost(TrayIconManager trayManager, Control host)
    {
        _trayManager = trayManager;
        _host = host;
    }

    public void Schedule()
    {
        if (_debounceTimer == null)
        {
            _debounceTimer = new Timer { Interval = 500 };
            _debounceTimer.Tick += (_, _) =>
            {
                _debounceTimer.Stop();
                Refresh();
            };
        }

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    public void Refresh()
    {
        if (_trayManager == null || _host == null)
            return;

        var generation = ++_generation;
        var trayDiscoverySids = _session.Database.Accounts
            .Where(a => a.TrayDiscovery)
            .Select(a => a.Sid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var credentials = _session.CredentialStore.Credentials
            .Where(c => trayDiscoverySids.Contains(c.Sid))
            .ToList();
        var existingApps = new HashSet<(string, string)>(
            _session.Database.Apps
                .Where(a => a is { IsFolder: false, IsUrlScheme: false } && !string.IsNullOrEmpty(a.ExePath))
                .Select(a => (a.ExePath, a.AccountSid)),
            CaseInsensitiveTupleComparer.Instance);

        _log.Info($"DiscoveryRefreshManager: scanning for discovered apps ({credentials.Count} account(s)).");
        var trayManager = _trayManager;
        Task.Run(() => _discoveryService.Scan(credentials, existingApps))
            .ContinueWith(t =>
            {
                if (_host.IsDisposed || !_host.IsHandleCreated || t.IsFaulted)
                    return;
                _host.BeginInvoke(() =>
                {
                    if (_host.IsDisposed || _generation != generation)
                        return;
                    trayManager.UpdateDiscoveredApps(t.Result);
                    _log.Info($"DiscoveryRefreshManager: discovered apps updated ({t.Result.Count} app(s)).");
                });
            });
    }

    public void Dispose()
    {
        _debounceTimer?.Stop();
        _debounceTimer?.Dispose();
        _debounceTimer = null;
    }
}