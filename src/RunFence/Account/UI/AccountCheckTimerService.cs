using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Infrastructure;
using Timer = System.Windows.Forms.Timer;

namespace RunFence.Account.UI;

/// <summary>
/// Manages the periodic account check timer for <see cref="Forms.AccountsPanel"/>,
/// detecting local account SID changes and notifying subscribers via events.
/// </summary>
public class AccountCheckTimerService : IDisposable
{
    private readonly ILocalUserProvider _localUserProvider;
    private readonly ISessionProvider _sessionProvider;
    private readonly ILoggingService _log;
    private readonly GrantReconciliationService? _reconciler;

    private Timer? _timer;
    private Dictionary<string, string>? _currentOsAccounts; // SID → username

    /// <summary>Raised when a SID change is detected that requires user attention.</summary>
    public event Action? SidChangeDetected;

    /// <summary>Raised when the accounts grid should be refreshed.</summary>
    public event Action? RefreshNeeded;

    public AccountCheckTimerService(
        ILocalUserProvider localUserProvider,
        ISessionProvider sessionProvider,
        ILoggingService log,
        GrantReconciliationService? reconciler = null)
    {
        _localUserProvider = localUserProvider;
        _sessionProvider = sessionProvider;
        _log = log;
        _reconciler = reconciler;
    }

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

        _inProcess = true;
        try
        {
            // Detect group membership changes and reconcile traverse grants asynchronously.
            // Done before the SID-change check so snapshot is updated even if no SID change.
            if (_reconciler != null)
            {
                var changedSids = await _reconciler.DetectGroupChanges();
                if (changedSids.Count > 0)
                {
                    _timer?.Stop();
                    var db = _sessionProvider.GetSession().Database;
                    var grantsSnapshot = db.Accounts.ToDictionary(
                        a => a.Sid, a => a.Grants.ToList(), StringComparer.OrdinalIgnoreCase);
                    _ = Task.Run(() => _reconciler.ReconcileChangedSids(changedSids, grantsSnapshot))
                        .ContinueWith(task =>
                        {
                            if (task.IsCompletedSuccessfully)
                            {
                                _reconciler.ApplyReconciliationResult(task.Result);
                                RefreshNeeded?.Invoke();
                            }
                            else if (task.Exception != null)
                            {
                                _log.Error("Grant reconciliation failed", task.Exception);
                            }

                            _timer?.Start();
                        }, TaskScheduler.FromCurrentSynchronizationContext());
                    return; // skip rest of tick; will fire again after reconciliation
                }
            }

            try
            {
                _localUserProvider.InvalidateCache();
                var currentAccounts = _localUserProvider.GetLocalUserAccounts();
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
                        var session = _sessionProvider.GetSession();
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
            catch
            {
                /* best effort */
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex.ToString());
        }
        finally
        {
            _inProcess = false;
        }
    }

    public void Stop() => _timer?.Stop();

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}