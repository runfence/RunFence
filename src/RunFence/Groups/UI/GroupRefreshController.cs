using RunFence.Core;
using RunFence.Infrastructure;
using Timer = System.Windows.Forms.Timer;

namespace RunFence.Groups.UI;

/// <summary>
/// Owns the 5-second refresh timer and the grid refresh task logic for <see cref="Forms.GroupsPanel"/>:
/// manages <c>_isMembersLoading</c> state, coordinates group and member population, and raises
/// <see cref="RefreshCompleted"/> so the panel can update the description field without an inner
/// async void that swallows exceptions.
/// </summary>
public class GroupRefreshController(
    GroupGridPopulator gridPopulator,
    ILoggingService log) : IDisposable
{
    private Timer? _refreshTimer;
    private bool _refreshInProgress;
    private Func<string?> _getSelectedGroupSid = null!;
    private Func<bool> _isRefreshing = null!;

    /// <summary>
    /// Raised on the UI thread after any refresh completes (timer-triggered or manual).
    /// The argument is the currently selected group SID (may differ from pre-refresh if the user changed selection).
    /// </summary>
    public event Action<string?>? RefreshCompleted;

    /// <summary>
    /// Raised when <c>IsRefreshing</c> state changes; the panel should update its <c>IsRefreshing</c> flag accordingly.
    /// </summary>
    public event Action<bool>? IsRefreshingChanged;

    /// <summary>
    /// Raised when <c>_isMembersLoading</c> changes; the panel should update the flag and call <c>UpdateButtonState</c>.
    /// </summary>
    public event Action<bool>? IsMembersLoadingChanged;

    /// <summary>
    /// Wires the panel callbacks required before any refresh operations.
    /// Must be called before <see cref="StartRefreshTimer"/> or <see cref="RefreshNow"/>.
    /// </summary>
    public void Initialize(Func<string?> getSelectedGroupSid, Func<bool> isRefreshing)
    {
        _getSelectedGroupSid = getSelectedGroupSid;
        _isRefreshing = isRefreshing;
    }

    /// <summary>
    /// Starts the 5-second auto-refresh timer if not already running.
    /// </summary>
    public void StartRefreshTimer()
    {
        if (_refreshTimer != null)
            return;
        log.Info("GroupRefreshController: starting group refresh timer.");
        _refreshTimer = new Timer { Interval = 5000 };
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshTimer.Start();
    }

    /// <summary>
    /// Stops the auto-refresh timer (e.g., when the panel becomes hidden).
    /// </summary>
    public void StopTimer() => _refreshTimer?.Stop();

    /// <summary>
    /// Resumes the auto-refresh timer (e.g., when the panel becomes visible again).
    /// </summary>
    public void ResumeTimer() => _refreshTimer?.Start();

    /// <summary>
    /// Stops and disposes the refresh timer.
    /// </summary>
    public void Dispose()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }

    /// <summary>
    /// Immediately triggers a full refresh of both the groups grid and the members grid,
    /// then raises <see cref="RefreshCompleted"/>. No-op (including no event) when already refreshing.
    /// </summary>
    public async Task RefreshNow()
    {
        if (_isRefreshing())
            return;
        await RefreshGridCore();
        RefreshCompleted?.Invoke(_getSelectedGroupSid());
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        if (_isRefreshing())
            return;
        RefreshGroupsOnly();
    }

    private async void RefreshGroupsOnly()
    {
        if (_isRefreshing() || _refreshInProgress) return;
        _refreshInProgress = true;
        try
        {
            IsRefreshingChanged?.Invoke(true);
            IsMembersLoadingChanged?.Invoke(true);
            var sidBeforeRefresh = _getSelectedGroupSid();
            try
            {
                await gridPopulator.PopulateGroups();
            }
            finally
            {
                IsRefreshingChanged?.Invoke(false);
                IsMembersLoadingChanged?.Invoke(false);
            }

            var currentSid = _getSelectedGroupSid();
            if (currentSid != sidBeforeRefresh)
                IsMembersLoadingChanged?.Invoke(true);
            RefreshCompleted?.Invoke(currentSid);
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private async Task RefreshGridCore()
    {
        IsRefreshingChanged?.Invoke(true);
        IsMembersLoadingChanged?.Invoke(true);
        var sidBeforeRefresh = _getSelectedGroupSid();
        try
        {
            var groupsTask = gridPopulator.PopulateGroups();
            var membersTask = sidBeforeRefresh != null
                ? gridPopulator.PopulateMembers(sidBeforeRefresh)
                : Task.CompletedTask;
            await Task.WhenAll(groupsTask, membersTask);
        }
        finally
        {
            IsRefreshingChanged?.Invoke(false);
            IsMembersLoadingChanged?.Invoke(false);
        }

        var currentSid = _getSelectedGroupSid();
        if (currentSid != sidBeforeRefresh)
            IsMembersLoadingChanged?.Invoke(true);
    }
}
