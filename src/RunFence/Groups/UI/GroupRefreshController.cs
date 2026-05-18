using RunFence.Core;
using Timer = System.Windows.Forms.Timer;

namespace RunFence.Groups.UI;

/// <summary>
/// Owns the 5-second refresh timer, the <see cref="IsRefreshing"/> state, and the grid refresh task
/// logic for <see cref="Forms.GroupsPanel"/>: coordinates group and member population, and raises
/// <see cref="RefreshCompleted"/> so the panel can update the description field without an inner
/// async void that swallows exceptions.
/// </summary>
public class GroupRefreshController(
    GroupGridPopulator gridPopulator,
    ILoggingService log) : IDisposable
{
    private Timer? _refreshTimer;
    private Func<string?> _getSelectedGroupSid = null!;

    /// <summary>
    /// Raised on the UI thread after any refresh completes (timer-triggered or manual).
    /// The argument is the currently selected group SID (may differ from pre-refresh if the user changed selection).
    /// </summary>
    public event Func<GroupRefreshCompletedInfo, Task>? RefreshCompleted;

    /// <summary>
    /// Raised when <see cref="IsRefreshing"/> state changes; the panel should call <c>UpdateButtonState</c> accordingly.
    /// </summary>
    public event Action<bool>? IsRefreshingChanged;

    /// <summary>
    /// Raised when <c>_isMembersLoading</c> changes; the panel should update the flag and call <c>UpdateButtonState</c>.
    /// </summary>
    public event Action<bool>? IsMembersLoadingChanged;

    /// <summary>
    /// True while a refresh operation is in progress. Used as a reentrancy guard.
    /// </summary>
    public bool IsRefreshing { get; private set; }
    public GroupRefreshRetryState? RetryState { get; private set; }

    /// <summary>
    /// Wires the panel callbacks required before any refresh operations.
    /// Must be called before <see cref="StartRefreshTimer"/> or <see cref="RefreshNow"/>.
    /// </summary>
    public void Initialize(Func<string?> getSelectedGroupSid)
    {
        _getSelectedGroupSid = getSelectedGroupSid;
    }

    /// <summary>
    /// Starts or resumes the 5-second auto-refresh timer.
    /// Creates the timer on first call; subsequent calls (e.g. after <see cref="StopTimer"/>) restart it.
    /// </summary>
    public void StartRefreshTimer()
    {
        if (_refreshTimer == null)
        {
            log.Info("GroupRefreshController: starting group refresh timer.");
            _refreshTimer = new Timer { Interval = 5000 };
            _refreshTimer.Tick += OnRefreshTimerTick;
        }
        _refreshTimer.Start();
    }

    /// <summary>
    /// Stops the auto-refresh timer (e.g., when the panel becomes hidden).
    /// </summary>
    public void StopTimer() => _refreshTimer?.Stop();

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
        if (IsRefreshing)
            return;
        string? sidBeforeRefresh = null;
        GroupRefreshCompletedInfo completionInfo;
        try
        {
            sidBeforeRefresh = _getSelectedGroupSid();
            completionInfo = await RefreshGridCore(sidBeforeRefresh);
            RetryState = null;
        }
        catch (Exception ex)
        {
            log.Error("Group refresh failed.", ex);
            IsRefreshing = false;
            IsRefreshingChanged?.Invoke(false);
            IsMembersLoadingChanged?.Invoke(false);
            RetryState = new GroupRefreshRetryState([], GroupRefreshRetryOperation.Refresh, ex.Message, DateTime.UtcNow);
            _refreshTimer?.Start();
            var sidAfterFailure = _getSelectedGroupSid != null ? _getSelectedGroupSid() : null;
            completionInfo = new GroupRefreshCompletedInfo(sidBeforeRefresh, sidAfterFailure, false);
        }
        if (RefreshCompleted is { } refreshCompleted)
        {
            try
            {
                foreach (Func<GroupRefreshCompletedInfo, Task> handler in refreshCompleted.GetInvocationList())
                    await handler(completionInfo);
            }
            catch (Exception ex)
            {
                log.Error("Group refresh completion handler failed.", ex);
            }
        }
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        if (IsRefreshing)
            return;
        RefreshGroupsOnly();
    }

    private async void RefreshGroupsOnly()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        IsRefreshingChanged?.Invoke(true);
        var sidBeforeRefresh = _getSelectedGroupSid();
        try
        {
            await gridPopulator.PopulateGroups();
            RetryState = null;
        }
        catch (Exception ex)
        {
            log.Error("Group refresh timer update failed.", ex);
            RetryState = new GroupRefreshRetryState([], GroupRefreshRetryOperation.Refresh, ex.Message, DateTime.UtcNow);
            _refreshTimer?.Start();
        }
        finally
        {
            IsRefreshing = false;
            IsRefreshingChanged?.Invoke(false);
        }

        var currentSid = _getSelectedGroupSid();
        if (currentSid != sidBeforeRefresh)
            IsMembersLoadingChanged?.Invoke(true);
        if (RefreshCompleted is { } refreshCompleted)
        {
            var completionInfo = new GroupRefreshCompletedInfo(sidBeforeRefresh, currentSid, false);
            try
            {
                foreach (Func<GroupRefreshCompletedInfo, Task> handler in refreshCompleted.GetInvocationList())
                    await handler(completionInfo);
            }
            catch (Exception ex)
            {
                log.Error("Group refresh completion handler failed.", ex);
            }
        }
    }

    private async Task<GroupRefreshCompletedInfo> RefreshGridCore(string? sidBeforeRefresh)
    {
        IsRefreshing = true;
        IsRefreshingChanged?.Invoke(true);
        IsMembersLoadingChanged?.Invoke(true);
        var membersWereRefreshed = false;
        try
        {
            var groupsTask = gridPopulator.PopulateGroups();
            var membersTask = sidBeforeRefresh != null
                ? gridPopulator.PopulateMembers(sidBeforeRefresh)
                : Task.CompletedTask;
            await Task.WhenAll(groupsTask, membersTask);
            membersWereRefreshed = sidBeforeRefresh != null;
            // On initial load (no prior selection), PopulateGroups selects the first group but
            // skips member loading. Explicitly load members for the newly-selected group.
            if (sidBeforeRefresh == null)
            {
                var initialSid = _getSelectedGroupSid();
                if (initialSid != null)
                {
                    await gridPopulator.PopulateMembers(initialSid);
                    membersWereRefreshed = true;
                }
            }
        }
        finally
        {
            IsRefreshing = false;
            IsRefreshingChanged?.Invoke(false);
            IsMembersLoadingChanged?.Invoke(false);
        }

        var currentSid = _getSelectedGroupSid();
        // Only signal pending member load when there was a prior selection and it changed
        // (means RefreshCompleted will call RefreshMembersGrid for the new SID).
        // When sidBeforeRefresh was null (initial load), members were already loaded above.
        if (sidBeforeRefresh != null && currentSid != sidBeforeRefresh)
            IsMembersLoadingChanged?.Invoke(true);

        return new GroupRefreshCompletedInfo(sidBeforeRefresh, currentSid, membersWereRefreshed);
    }
}
