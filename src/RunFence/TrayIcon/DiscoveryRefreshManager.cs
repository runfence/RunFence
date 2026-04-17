using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using Timer = System.Windows.Forms.Timer;

namespace RunFence.TrayIcon;

/// <summary>
/// Manages start-menu discovery scanning and updates the tray with discovered apps.
/// Supports both debounced (scheduled) and immediate refresh.
/// </summary>
public sealed class DiscoveryRefreshManager(StartMenuDiscoveryService discoveryService, ISessionProvider sessionProvider, ILoggingService log) : IDisposable
{
    private TrayIconManager? _trayManager;
    private Control? _host;
    private Timer? _debounceTimer;
    private int _generation;

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
        var session = sessionProvider.GetSession();
        var snapshot = session.Database.CreateSnapshot();
        var trayDiscoverySids = snapshot.Accounts
            .Where(a => a.TrayDiscovery)
            .Select(a => a.Sid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var credentials = session.CredentialStore.Credentials
            .Where(c => trayDiscoverySids.Contains(c.Sid))
            .ToList();
        var existingApps = new HashSet<(string, string)>(
            snapshot.Apps
                .Where(a => a is { IsFolder: false, IsUrlScheme: false } && !string.IsNullOrEmpty(a.ExePath))
                .Select(a => (a.ExePath, a.AccountSid)),
            CaseInsensitiveTupleComparer.Instance);

        log.Info($"DiscoveryRefreshManager: scanning for discovered apps ({credentials.Count} account(s)).");
        var trayManager = _trayManager;
        var host = _host;
        Task.Run(() => discoveryService.Scan(credentials, existingApps))
            .ContinueWith(t =>
            {
                if (t.IsCanceled || t.IsFaulted)
                    return;
                try
                {
                    host.BeginInvoke(() =>
                    {
                        if (host.IsDisposed || !host.IsHandleCreated || _generation != generation)
                            return;
                        trayManager.UpdateDiscoveredApps(t.Result);
                        log.Info($"DiscoveryRefreshManager: discovered apps updated ({t.Result.Count} app(s)).");
                    });
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            });
    }

    public void Dispose()
    {
        _debounceTimer?.Stop();
        _debounceTimer?.Dispose();
        _debounceTimer = null;
    }
}