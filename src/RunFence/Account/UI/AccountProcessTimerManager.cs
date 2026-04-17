using RunFence.Core;
using Timer = System.Windows.Forms.Timer;

namespace RunFence.Account.UI;

/// <summary>
/// Manages process-related timers for the accounts panel: periodic process list refresh
/// and periodic check for which accounts have running processes (for expand indicators).
/// </summary>
public class AccountProcessTimerManager(ILoggingService log) : IDisposable
{
    private AccountGridProcessExpander _processExpander = null!;
    private DataGridView _grid = null!;
    private Func<bool> _isSortActive = null!;
    private Func<bool> _isVisibleAndParentVisible = null!;

    private Timer? _processRefreshTimer;
    private Timer? _processCheckTimer;
    private bool _wasMinimized;
    private bool _started;

    public void Initialize(DataGridView grid, AccountGridProcessExpander processExpander,
        Func<bool> isSortActive)
    {
        _grid = grid;
        _processExpander = processExpander;
        _isSortActive = isSortActive;
    }

    public void Start(Func<bool> isVisibleAndParentVisible)
    {
        if (_started)
            return;
        _started = true;
        _isVisibleAndParentVisible = isVisibleAndParentVisible;

        log.Info("AccountsPanel: starting process refresh timers.");
        _processRefreshTimer = new Timer { Interval = 2000 };
        _processRefreshTimer.Tick += OnProcessRefreshTimerTick;

        _processCheckTimer = new Timer { Interval = 5000 };
        _processCheckTimer.Tick += OnProcessCheckTimerTick;

        if (_isVisibleAndParentVisible())
        {
            _processRefreshTimer.Start();
            _processCheckTimer.Start();
        }
    }

    public void HandleVisibilityChanged(bool isVisible)
    {
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
        _processCheckTimer?.Stop();
        _processRefreshTimer?.Stop();
        try
        {
            OnProcessCheckTimerTick(null, EventArgs.Empty);
            OnProcessRefreshTimerTick(null, EventArgs.Empty);
        }
        finally
        {
            _processCheckTimer?.Start();
            _processRefreshTimer?.Start();
        }
    }

    /// <summary>
    /// Triggers a delayed process refresh after a specified interval.
    /// </summary>
    public void TriggerDelayedRefresh(int delayMs)
    {
        if (delayMs <= 0)
        {
            TriggerImmediateRefresh();
            return;
        }

        var timer = new Timer { Interval = delayMs };
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
        var sidList = expandedSids as List<string> ?? expandedSids.ToList();
        return await Task.Run(() => _processExpander.FetchRefreshData(sidList));
    }

    private async void OnProcessRefreshTimerTick(object? sender, EventArgs e)
    {
        if (!_isVisibleAndParentVisible() || !_processExpander.HasExpandedRows || _isSortActive())
            return;
        _processRefreshTimer!.Stop();
        try
        {
            var sids = _processExpander.GetExpandedSidSnapshot();
            var data = await Task.Run(() => _processExpander.FetchRefreshData(sids));
            if (_isVisibleAndParentVisible() && !_grid.IsDisposed && !_isSortActive())
                _processExpander.ApplyRefreshData(data);
        }
        finally
        {
            _processRefreshTimer!.Start();
        }
    }

    private async void OnProcessCheckTimerTick(object? sender, EventArgs e)
    {
        if (!_isVisibleAndParentVisible() || _isSortActive())
            return;
        bool fromTimer = sender != null;
        if (fromTimer)
            _processCheckTimer!.Stop();
        try
        {
            var sids = _grid.Rows.Cast<DataGridViewRow>()
                .Select(r => AccountGridProcessExpander.GetSidFromRow(r))
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var withProcesses = await Task.Run(() => _processExpander.FetchSidsWithProcesses(sids));
            if (_isVisibleAndParentVisible() && !_grid.IsDisposed && !_isSortActive())
                _processExpander.ApplySidsWithProcesses(withProcesses);
        }
        finally
        {
            if (fromTimer)
                _processCheckTimer!.Start();
        }
    }

    public void Dispose()
    {
        _processRefreshTimer?.Stop();
        _processRefreshTimer?.Dispose();
        _processCheckTimer?.Stop();
        _processCheckTimer?.Dispose();
    }
}