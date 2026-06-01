namespace RunFence.Apps.UI;

public class ApplicationsPanelRefreshCoordinator
{
    private readonly ApplicationsGridPopulator _gridPopulator;
    private readonly AppGridDragDropHandler _dragDropHandler;
    private readonly ApplicationsPanelSaveHelper _saveHelper;
    private readonly ApplicationsHandlerSyncHelper? _handlerSyncHelper;
    private IApplicationsPanelRefreshView? _view;

    public ApplicationsPanelRefreshCoordinator(
        ApplicationsGridPopulator gridPopulator,
        AppGridDragDropHandler dragDropHandler,
        ApplicationsPanelSaveHelper saveHelper,
        ApplicationsHandlerSyncHelper? handlerSyncHelper = null)
    {
        _gridPopulator = gridPopulator;
        _dragDropHandler = dragDropHandler;
        _saveHelper = saveHelper;
        _handlerSyncHelper = handlerSyncHelper;
    }

    public void Initialize(IApplicationsPanelRefreshView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (_view != null)
            throw new InvalidOperationException("ApplicationsPanelRefreshCoordinator is already initialized.");

        _view = view;
    }

    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RefreshCore();
        return Task.CompletedTask;
    }

    public Task SaveRefreshAndReselectAsync(string? appId, CancellationToken cancellationToken)
        => SaveRefreshAndReselectAsync(appId, -1, targetedSave: false, cancellationToken);

    public Task SaveRefreshAndReselectAsync(
        string? appId,
        int fallbackIndex,
        bool targetedSave,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _handlerSyncHelper?.CleanupOrphanedMappings();
        if (targetedSave && appId != null)
            _saveHelper.SaveForApp(appId);
        else
            _saveHelper.SaveAll();

        RefreshAfterInMemoryMutation(appId, fallbackIndex);
        return Task.CompletedTask;
    }

    public void RefreshAfterInMemoryMutation(string? appId = null, int fallbackIndex = -1)
    {
        var view = EnsureInitialized();
        RefreshCore();
        if (appId != null)
            view.SelectAppById(appId);
        else if (fallbackIndex >= 0)
            view.SelectRowByIndex(fallbackIndex);
        else
            view.SelectFirstRow();

        view.PublishDataChanged();
    }

    private void RefreshCore()
    {
        var view = EnsureInitialized();
        _gridPopulator.PopulateGrid(_dragDropHandler, view.SetIsRefreshing, view.ReapplyGlyphIfActive);
        view.UpdateButtonState();
    }

    private IApplicationsPanelRefreshView EnsureInitialized()
    {
        if (_view == null)
            throw new InvalidOperationException("ApplicationsPanelRefreshCoordinator must be initialized before use.");

        return _view;
    }
}
