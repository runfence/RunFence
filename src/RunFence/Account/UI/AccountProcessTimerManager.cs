using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Account.UI;

/// <summary>
/// Manages process-related timers for the accounts panel: periodic process list refresh
/// and periodic check for which accounts have running processes (for expand indicators).
/// </summary>
public class AccountProcessTimerManager(ILoggingService log, IUiTimerFactory uiTimerFactory) : IDisposable
{
    private AccountGridProcessExpander _processExpander = null!;
    private DataGridView _grid = null!;
    private Func<bool> _isSortActive = null!;
    private Func<bool> _isVisibleAndParentVisible = null!;

    private IUiTimer? _processRefreshTimer;
    private IUiTimer? _processCheckTimer;
    private CancellationTokenSource? _processRefreshCts;
    private CancellationTokenSource? _processCheckCts;
    private long _processRefreshGeneration;
    private long _processCheckGeneration;
    private bool _wasMinimized;
    private bool _started;
    private bool _disposed;

    public void Initialize(DataGridView grid, AccountGridProcessExpander processExpander,
        Func<bool> isSortActive)
    {
        _grid = grid;
        _processExpander = processExpander;
        _isSortActive = isSortActive;
    }

    public void Start(Func<bool> isVisibleAndParentVisible)
    {
        if (_disposed)
            return;
        if (_started)
            return;
        _started = true;
        _isVisibleAndParentVisible = isVisibleAndParentVisible;

        log.Info("AccountsPanel: starting process refresh timers.");
        _processRefreshTimer = uiTimerFactory.Create();
        _processRefreshTimer.Interval = 2000;
        _processRefreshTimer.Tick += (_, _) => OnProcessRefreshTimerTick();

        _processCheckTimer = uiTimerFactory.Create();
        _processCheckTimer.Interval = 5000;
        _processCheckTimer.Tick += (_, _) => OnProcessCheckTimerTick();

        if (_isVisibleAndParentVisible())
        {
            _processRefreshTimer.Start();
            _processCheckTimer.Start();
        }
    }

    public void HandleVisibilityChanged(bool isVisible)
    {
        if (_disposed)
            return;

        if (isVisible)
        {
            _processRefreshTimer?.Start();
            _processCheckTimer?.Start();
        }
        else
        {
            _processRefreshTimer?.Stop();
            _processCheckTimer?.Stop();
        }
    }

    public void HandleParentFormResize(bool isMinimized)
    {
        if (_disposed)
            return;

        bool justRestored = _wasMinimized && !isMinimized;
        _wasMinimized = isMinimized;

        if (justRestored && _isVisibleAndParentVisible() && !_isSortActive())
            TriggerImmediateRefresh();
    }

    /// <summary>
    /// Triggers immediate process check and refresh. Used by RefreshOnActivation and TriggerProcessRefresh.
    /// Stops both timers before invoking handlers to prevent overlapping ticks, then restarts both in
    /// finally. The refresh timer handler also self-restarts in its own finally after async work completes,
    /// which harmlessly resets the countdown interval.
    /// </summary>
    public void TriggerImmediateRefresh()
    {
        if (_disposed)
            return;

        _processCheckTimer?.Stop();
        _processRefreshTimer?.Stop();
        try
        {
            _ = RunProcessCheckAsync(fromTimer: false);
            _ = RunProcessRefreshAsync();
        }
        finally
        {
            var processCheckTimer = _processCheckTimer;
            if (processCheckTimer != null && CanRestartTimer(processCheckTimer))
                processCheckTimer.Start();

            var processRefreshTimer = _processRefreshTimer;
            if (processRefreshTimer != null && CanRestartTimer(processRefreshTimer, requireExpandedRows: true))
                processRefreshTimer.Start();
        }
    }

    /// <summary>
    /// Triggers a delayed process refresh after a specified interval.
    /// </summary>
    public void TriggerDelayedRefresh(int delayMs)
    {
        if (_disposed)
            return;

        if (delayMs <= 0)
        {
            TriggerImmediateRefresh();
            return;
        }

        IUiTimer? timer = null;
        timer = uiTimerFactory.Create();
        timer.Interval = delayMs;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            TriggerImmediateRefresh();
        };
        timer.Start();
    }

    /// <summary>
    /// Gets the snapshot of expanded SIDs for process data prefetching during grid refresh.
    /// Returns null if sort is active.
    /// </summary>
    public IReadOnlyList<string>? GetExpandedSidsForRefresh()
    {
        return !_isSortActive() ? _processExpander.GetExpandedSidSnapshot() : null;
    }

    /// <summary>
    /// Fetches process refresh data for the given expanded SIDs on a background thread.
    /// </summary>
    public async Task<Dictionary<string, IReadOnlyList<ProcessInfo>>> FetchRefreshDataAsync(IReadOnlyList<string> expandedSids)
    {
        if (_disposed)
            return new Dictionary<string, IReadOnlyList<ProcessInfo>>(StringComparer.OrdinalIgnoreCase);

        var sidList = expandedSids as List<string> ?? expandedSids.ToList();
        var generation = Interlocked.Increment(ref _processRefreshGeneration);
        _processRefreshCts?.Cancel();
        _processRefreshCts?.Dispose();
        _processRefreshCts = new CancellationTokenSource();
        var ct = _processRefreshCts.Token;
        try
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var data = _processExpander.FetchRefreshData(sidList, ct);
                if (_disposed || generation != Volatile.Read(ref _processRefreshGeneration))
                    throw new OperationCanceledException(ct);
                return data;
            }, ct);
        }
        catch (OperationCanceledException)
        {
            return new Dictionary<string, IReadOnlyList<ProcessInfo>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async void OnProcessRefreshTimerTick()
        => await RunProcessRefreshAsync();

    private async Task RunProcessRefreshAsync()
    {
        if (!CanRestartTimer(_processRefreshTimer, requireExpandedRows: true))
            return;

        _processRefreshTimer!.Stop();
        var generation = Interlocked.Increment(ref _processRefreshGeneration);
        _processRefreshCts?.Cancel();
        _processRefreshCts?.Dispose();
        _processRefreshCts = new CancellationTokenSource();
        var ct = _processRefreshCts.Token;
        try
        {
            var sids = _processExpander.GetExpandedSidSnapshot();
            var data = await Task.Run(() => _processExpander.FetchRefreshData(sids, ct), ct);
            if (generation != Volatile.Read(ref _processRefreshGeneration))
                return;
            if (CanRestartTimer(_processRefreshTimer, requireExpandedRows: true))
                _processExpander.ApplyRefreshData(data);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            log.Error("Account process refresh failed.", ex);
        }
        finally
        {
            if (CanRestartTimer(_processRefreshTimer, requireExpandedRows: true))
                _processRefreshTimer.Start();
        }
    }

    private async void OnProcessCheckTimerTick()
        => await RunProcessCheckAsync(fromTimer: true);

    private async Task RunProcessCheckAsync(bool fromTimer)
    {
        if (!CanRestartTimer(_processCheckTimer))
            return;

        if (fromTimer)
            _processCheckTimer!.Stop();
        var generation = Interlocked.Increment(ref _processCheckGeneration);
        _processCheckCts?.Cancel();
        _processCheckCts?.Dispose();
        _processCheckCts = new CancellationTokenSource();
        var ct = _processCheckCts.Token;
        try
        {
            var sids = _grid.Rows.Cast<DataGridViewRow>()
                .Select(r => AccountGridProcessExpander.GetSidFromRow(r))
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var withProcesses = await Task.Run(() => _processExpander.FetchSidsWithProcesses(sids, ct), ct);
            if (generation != Volatile.Read(ref _processCheckGeneration))
                return;
            if (CanRestartTimer(_processCheckTimer))
                _processExpander.ApplySidsWithProcesses(withProcesses);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            log.Error("Account process check failed.", ex);
        }
        finally
        {
            var processCheckTimer = _processCheckTimer;
            if (fromTimer && processCheckTimer != null && CanRestartTimer(processCheckTimer))
                processCheckTimer.Start();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _started = false;

        var refreshCts = _processRefreshCts;
        var checkCts = _processCheckCts;
        _processRefreshCts = null;
        _processCheckCts = null;

        refreshCts?.Cancel();
        checkCts?.Cancel();

        var refreshTimer = _processRefreshTimer;
        var checkTimer = _processCheckTimer;
        _processRefreshTimer = null;
        _processCheckTimer = null;

        refreshTimer?.Stop();
        checkTimer?.Stop();
        refreshTimer?.Dispose();
        checkTimer?.Dispose();
        refreshCts?.Dispose();
        checkCts?.Dispose();
    }

    private bool CanRestartTimer(IUiTimer? timer, bool requireExpandedRows = false)
    {
        if (_disposed || timer == null || _grid.IsDisposed)
            return false;
        if (!_isVisibleAndParentVisible() || _isSortActive())
            return false;

        return !requireExpandedRows || _processExpander.HasExpandedRows;
    }
}
