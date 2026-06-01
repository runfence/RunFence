namespace RunFence.Account.UI;

public class AccountsPanelLifecycleCoordinator(
    IAccountsPanelTimerCoordinator timerCoordinator,
    IAccountsPanelProcessRefreshController processDisplayManager)
{
    private IAccountsPanelLifecycleView? _view;
    private Form? _parentForm;
    private bool _eventsWired;
    private bool _loadedOnce;

    public void Initialize(IAccountsPanelLifecycleView view)
    {
        _view = view;
        if (_eventsWired)
            return;

        _eventsWired = true;
        timerCoordinator.SidChangeDetected += view.ShowSidMigrationWarning;
        timerCoordinator.RefreshNeeded += async () =>
        {
            if (!view.IsRefreshing && !view.IsOperationInProgress)
                await view.RefreshGridAsync();
        };
    }

    public void Initialize()
    {
        var view = GetView();
        if (!_loadedOnce)
        {
            _loadedOnce = true;
            _ = view.InitialRefreshAsync();
            StartProcessRefresh();
            return;
        }

        _ = view.RefreshGridAsync();
    }

    public void OnParentChanged(Control? parent)
    {
        if (_parentForm != null)
            _parentForm.Resize -= OnParentFormResize;

        _parentForm = parent as Form ?? parent?.FindForm();
        if (_parentForm != null)
            _parentForm.Resize += OnParentFormResize;
    }

    public void OnVisibleChanged(bool visible)
    {
        var view = GetView();
        var isVisible = visible && view.IsParentFormVisible();
        timerCoordinator.NotifyVisibilityChanged(isVisible);
        processDisplayManager.NotifyVisibilityChanged(isVisible);
        if (isVisible)
            _ = RefreshOnActivationAsync();
    }

    public void OnResize()
        => processDisplayManager.NotifyParentResized(_parentForm?.WindowState == FormWindowState.Minimized);

    public void StartProcessRefresh()
    {
        var view = GetView();
        timerCoordinator.Start();
        processDisplayManager.Start(() => view.Visible && view.IsParentFormVisible());
    }

    public void StopProcessRefresh()
    {
        timerCoordinator.Stop();
        processDisplayManager.NotifyVisibilityChanged(false);
    }

    public async Task RefreshOnActivationAsync()
    {
        var view = GetView();
        if (!_loadedOnce || view.IsRefreshing || view.IsOperationInProgress || view.IsSortActive)
            return;

        await view.RefreshGridAsync();
        if (!view.IsSortActive)
            processDisplayManager.TriggerImmediateRefresh();
    }

    private void OnParentFormResize(object? sender, EventArgs e)
        => OnResize();

    private IAccountsPanelLifecycleView GetView()
        => _view ?? throw new InvalidOperationException("AccountsPanelLifecycleCoordinator must be initialized before use.");
}
