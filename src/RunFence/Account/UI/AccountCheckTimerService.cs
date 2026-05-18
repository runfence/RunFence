using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Infrastructure;

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
    IAccountGrantReconciliationRunner reconciliationRunner,
    IUiTimerFactory uiTimerFactory)
    : IDisposable
{
    private IUiTimer? _timer;
    private bool _disposed;
    private bool _visible = true;
    private Dictionary<string, string>? _currentOsAccounts; // SID → username

    /// <summary>Raised when a SID change is detected that requires user attention.</summary>
    public event Action? SidChangeDetected;

    /// <summary>Raised when the accounts grid should be refreshed.</summary>
    public event Action? RefreshNeeded;

    public void Start()
    {
        if (_disposed || _timer != null)
            return;

        _timer = uiTimerFactory.Create();
        _timer.Interval = 3000;
        _timer.Tick += (_, _) => OnTimerTick();
        _timer.Start();
    }

    public void HandleVisibilityChanged(bool visible)
    {
        _visible = visible;

        if (_disposed)
            return;
        if (_timer == null)
            return;

        if (visible)
            _timer.Start();
        else
            _timer.Stop();
    }

    private bool _inProcess;

    private void OnTimerTick() => OnTick(null, EventArgs.Empty);

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_disposed)
            return;
        if (_inProcess)
            return;
        if (reconciliationGuard.IsInProgress)
            return;

        _inProcess = true;
        // Set the guard before any await so that a concurrent manual refresh cannot start
        // reconciliation during the DetectGroupChanges async gap on the UI thread.
        reconciliationGuard.IsInProgress = true;
        try
        {
            // Detect group membership changes and reconcile traverse grants asynchronously.
            // Done before the SID-change check so snapshot is updated even if no SID change.
            var changedSids = await reconciliationRunner.DetectGroupChanges();
            if (changedSids.Count > 0)
            {
                _timer?.Stop();
                var db = sessionProvider.GetSession().Database;
                var grantsSnapshot = db.Accounts.ToDictionary(
                    account => account.Sid,
                    account => (IReadOnlyList<RunFence.Core.Models.GrantedPathEntry>)account.Grants
                        .Select(entry => entry.Clone())
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase);
                await RunGrantReconciliationAsync(changedSids, grantsSnapshot);
                return;
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
            reconciliationGuard.IsInProgress = false;
        }
    }

    public void Stop() => _timer?.Stop();

    public void Dispose()
    {
        _disposed = true;
        reconciliationGuard.IsInProgress = false;
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
    }

    private async Task RunGrantReconciliationAsync(
        List<(string Sid, List<string> NewGroups)> changedSids,
        IReadOnlyDictionary<string, IReadOnlyList<RunFence.Core.Models.GrantedPathEntry>> grantsSnapshot)
    {
        try
        {
            var result = await reconciliationRunner.ReconcileChangedSidsAsync(changedSids, grantsSnapshot);
            if (_disposed)
                return;

            reconciliationRunner.ApplyReconciliationResult(result);
            RefreshNeeded?.Invoke();
        }
        catch (Exception ex)
        {
            log.Error("Grant reconciliation failed", ex);
        }
        finally
        {
            RestartTimerIfVisible();
        }
    }

    private void RestartTimerIfVisible()
    {
        if (_disposed || !_visible)
            return;

        _timer?.Start();
    }
}
