using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Account.UI;

/// <summary>
/// Handles async SID resolution, stale-name updates, cancellation-safe grid refresh,
/// and row-selection-preserving grid population for the accounts panel.
/// Extracted from <see cref="Forms.AccountsPanel"/> to keep the panel focused on UI orchestration.
/// </summary>
public class AccountGridRefreshHandler(
    IAccountSidResolutionService sidResolution,
    SessionPersistenceHelper persistenceHelper,
    ILoggingService log,
    AccountGridPopulator gridPopulator,
    ISessionProvider sessionProvider,
    GrantReconciliationService reconciler,
    ReconciliationGuard reconciliationGuard)
{
    private DataGridView _grid = null!;
    private IGridSortState _sortState = null!;
    private IAccountGridCallbacks _callbacks = null!;

    private CancellationTokenSource? _refreshCts;

    private string? InteractiveUserSid { get; set; }

    public void Initialize(DataGridView grid, IGridSortState sortState, IAccountGridCallbacks callbacks)
    {
        _grid = grid;
        _sortState = sortState;
        _callbacks = callbacks;
        gridPopulator.Initialize(grid);
    }

    public async Task InitialRefreshAsync()
    {
        log.Info("AccountGridRefreshHandler: initial SID resolution starting.");
        _callbacks.SetIsRefreshing();
        _grid.CancelEdit();
        _grid.Rows.Clear();

        try
        {
            var resolutions = await ResolveSidsAsync();

            InteractiveUserSid = await Task.Run(() => NativeTokenHelper.TryGetInteractiveUserSid()?.Value);

            if (_grid.IsDisposed)
                return;

            var session = sessionProvider.GetSession();
            var db = session.Database;
            persistenceHelper.ApplyStaleNameUpdates(resolutions, db, session.PinDerivedKey, session.CredentialStore.ArgonSalt);

            foreach (var kvp in resolutions)
                session.SidNameCache[kvp.Key] = kvp.Value;

            var displayNameCache = BuildDisplayNameCache(resolutions);
            PopulateGrid(displayNameCache, resolutions);
            log.Info($"AccountGridRefreshHandler: initial SID resolution complete ({resolutions.Count} SID(s)).");
        }
        catch (Exception ex)
        {
            log.Error("Async stale name detection failed, falling back to synchronous refresh", ex);
            RefreshGrid();
        }
    }

    public async void RefreshGrid(Action? afterPopulate = null)
        => await RefreshGridCoreAsync(beforePopulate: null, afterPopulate);

    public async void RefreshGridWithPreFetch(Func<Task> beforePopulate, Action? afterPopulate = null)
        => await RefreshGridCoreAsync(beforePopulate, afterPopulate);

    private async Task RefreshGridCoreAsync(Func<Task>? beforePopulate, Action? afterPopulate)
    {
        // Cancel any previous in-flight refresh and start a new one.
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        var cts = new CancellationTokenSource();
        _refreshCts = cts;

        // Reconcile group membership changes before populating the grid.
        // If the timer is already reconciling, skip to avoid concurrent reconciliation.
        // If the timer already reconciled, ReconcileIfGroupsChanged is a no-op (snapshot already updated).
        var session = sessionProvider.GetSession();
        var db = session.Database;
        if (!reconciliationGuard.IsInProgress)
        {
            reconciliationGuard.IsInProgress = true;
            try
            {
                await reconciler.ReconcileIfGroupsChanged();
            }
            finally
            {
                reconciliationGuard.IsInProgress = false;
            }
        }

        Dictionary<string, string?> sidResolutions;
        try
        {
            sidResolutions = await ResolveSidsAsync();
        }
        catch (Exception ex)
        {
            log.Error("Async SID resolution in RefreshGrid failed, using empty resolutions", ex);
            sidResolutions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        // If a newer refresh was requested while we were awaiting, discard this result.
        if (cts.IsCancellationRequested || _grid.IsDisposed)
            return;

        // Pre-fetch any data needed before clearing the grid (e.g. process lists).
        if (beforePopulate != null)
        {
            await beforePopulate();
            if (cts.IsCancellationRequested || _grid.IsDisposed)
                return;
        }

        persistenceHelper.ApplyStaleNameUpdates(sidResolutions, db, session.PinDerivedKey, session.CredentialStore.ArgonSalt);

        var displayNameCache = BuildDisplayNameCache(sidResolutions);
        PopulateGrid(displayNameCache, sidResolutions);
        afterPopulate?.Invoke();
    }

    private void PopulateGrid(Dictionary<Guid, string> displayNameCache, Dictionary<string, string?>? sidResolutions = null)
    {
        Guid? selectedCredId = null;
        string? selectedSid = null;
        string? selectedContainerName = null;

        if (_grid.SelectedRows.Count > 0)
        {
            switch (_grid.SelectedRows[0].Tag)
            {
                case AccountRow prevRow:
                {
                    selectedCredId = prevRow.Credential?.Id;
                    if (!selectedCredId.HasValue)
                        selectedSid = prevRow.Sid;
                    break;
                }
                case ContainerRow prevContainer:
                    selectedContainerName = prevContainer.Container.Name;
                    break;
            }
        }

        _callbacks.SetIsRefreshing();
        _grid.CancelEdit();
        _grid.Rows.Clear();
        _callbacks.ClearStatus();

        var session = sessionProvider.GetSession();
        gridPopulator.Build(new PopulateData(session.Database, session.CredentialStore, displayNameCache, sidResolutions,
            InteractiveUserSid, _sortState.IsSortActive, _sortState.SortColumnIndex, _sortState.SortDescending));

        _callbacks.ClearIsRefreshing();
        _callbacks.ReapplyGlyph();

        bool selectionRestored = false;
        if (selectedCredId.HasValue || selectedSid != null || selectedContainerName != null)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                bool match = row.Tag switch
                {
                    AccountRow ar => (selectedCredId.HasValue && ar.Credential?.Id == selectedCredId) ||
                                     (selectedSid != null && string.Equals(ar.Sid, selectedSid, StringComparison.OrdinalIgnoreCase)),
                    ContainerRow cr => selectedContainerName != null && string.Equals(cr.Container.Name, selectedContainerName, StringComparison.OrdinalIgnoreCase),
                    _ => false
                };

                if (match)
                {
                    row.Selected = true;
                    _grid.CurrentCell = row.Cells["Account"];
                    selectionRestored = true;
                    break;
                }
            }
        }

        if (!selectionRestored)
            _callbacks.SelectFirstRow();

        _callbacks.UpdateButtonState();
    }

    private Task<Dictionary<string, string?>> ResolveSidsAsync()
    {
        var session = sessionProvider.GetSession();
        return sidResolution.ResolveSidsAsync(session.CredentialStore, session.Database.SidNames);
    }

    private Dictionary<Guid, string> BuildDisplayNameCache(Dictionary<string, string?> resolutions)
    {
        var session = sessionProvider.GetSession();
        return sidResolution.BuildDisplayNameCache(session.CredentialStore, resolutions, session.Database.SidNames);
    }
}