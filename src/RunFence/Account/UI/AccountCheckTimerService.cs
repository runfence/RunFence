using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Infrastructure;
using Timer = System.Windows.Forms.Timer;

namespace RunFence.Account.UI;

/// <summary>
/// Manages the periodic account check timer for <see cref="Forms.AccountsPanel"/>,
/// detecting local account SID changes and notifying subscribers via events.
/// </summary>
public class AccountCheckTimerService(
    ILocalUserProvider localUserProvider,
    ISessionProvider sessionProvider,
    ILoggingService log,
    ReconciliationGuard reconciliationGuard,
    GrantReconciliationService? reconciler = null)
    : IDisposable
{
    private Timer? _timer;
    private Dictionary<string, string>? _currentOsAccounts; // SID → username

    /// <summary>Raised when a SID change is detected that requires user attention.</summary>
    public event Action? SidChangeDetected;

    /// <summary>Raised when the accounts grid should be refreshed.</summary>
    public event Action? RefreshNeeded;

    public void Start()
    {
        if (_timer != null)
            return;

        _timer = new Timer { Interval = 3000 };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void HandleVisibilityChanged(bool visible)
    {
        if (_timer == null)
            return;

        if (visible)
            _timer.Start();
        else
            _timer.Stop();
    }

    private bool _inProcess;

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_inProcess)
            return;
        if (reconciliationGuard.IsInProgress)
            return;

        _inProcess = true;
        // Set the guard before any await so that a concurrent manual refresh cannot start
        // reconciliation during the DetectGroupChanges async gap on the UI thread.
        if (reconciler != null)
            reconciliationGuard.IsInProgress = true;
        var reconciliationDispatched = false;
        try
        {
            // Detect group membership changes and reconcile traverse grants asynchronously.
            // Done before the SID-change check so snapshot is updated even if no SID change.
            if (reconciler != null)
            {
                var changedSids = await reconciler.DetectGroupChanges();
                if (changedSids.Count > 0)
                {
                    _timer?.Stop();
                    var db = sessionProvider.GetSession().Database;
                    var grantsSnapshot = db.Accounts.ToDictionary(
                        a => a.Sid, a => a.Grants.ToList(), StringComparer.OrdinalIgnoreCase);
                    reconciliationDispatched = true;
                    _ = Task.Run(() => reconciler.ReconcileChangedSids(changedSids, grantsSnapshot))
                        .ContinueWith(task =>
                        {
                            reconciliationGuard.IsInProgress = false;
                            if (task.IsCompletedSuccessfully)
                            {
                                reconciler.ApplyReconciliationResult(task.Result);
                                RefreshNeeded?.Invoke();
                            }
                            else if (task.Exception != null)
                            {
                                log.Error("Grant reconciliation failed", task.Exception);
                            }

                            _timer?.Start();
                        }, TaskScheduler.FromCurrentSynchronizationContext());
                    return; // skip rest of tick; will fire again after reconciliation
                }
            }

            try
            {
                localUserProvider.InvalidateCache();
                var currentAccounts = localUserProvider.GetLocalUserAccounts();
                var current = currentAccounts.ToDictionary(
                    u => u.Sid, u => u.Username, StringComparer.OrdinalIgnoreCase);

                if (_currentOsAccounts != null)
                {
                    var currentSids = current.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var previousSids = _currentOsAccounts.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var sidsChanged = !previousSids.SetEquals(currentSids);

                    var namesChanged = !sidsChanged && _currentOsAccounts.Any(kvp =>
                        current.TryGetValue(kvp.Key, out var newName) &&
                        !string.Equals(kvp.Value, newName, StringComparison.OrdinalIgnoreCase));

                    if (sidsChanged)
                    {
                        var session = sessionProvider.GetSession();
                        var db = session.Database;
                        var credentialStore = session.CredentialStore;

                        var hasSidChange = credentialStore.Credentials.Any(c =>
                            c is { IsCurrentAccount: false, IsInteractiveUser: false } &&
                            !string.IsNullOrEmpty(c.Sid) &&
                            !currentSids.Contains(c.Sid) &&
                            db.SidNames.TryGetValue(c.Sid, out var mapName) &&
                            currentAccounts.Any(a => string.Equals(a.Username, SidNameResolver.ExtractUsername(mapName), StringComparison.OrdinalIgnoreCase)));

                        if (hasSidChange)
                            SidChangeDetected?.Invoke();
                    }

                    if (sidsChanged || namesChanged)
                    {
                        _currentOsAccounts = current;
                        RefreshNeeded?.Invoke();
                    }
                }
                else
                {
                    _currentOsAccounts = current;
                }
            }
            catch (Exception ex)
            {
                log.Debug($"SID change detection failed: {ex}");
            }
        }
        catch (Exception ex)
        {
            log.Error(ex.ToString());
        }
        finally
        {
            _inProcess = false;
            if (!reconciliationDispatched)
                reconciliationGuard.IsInProgress = false;
        }
    }

    public void Stop() => _timer?.Stop();

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}