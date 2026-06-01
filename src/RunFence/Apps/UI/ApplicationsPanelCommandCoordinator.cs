using RunFence.Apps.UI.Forms;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Apps.UI;

public class ApplicationsPanelCommandCoordinator
{
    private readonly ApplicationsCrudOrchestrator _crudOrchestrator;
    private readonly ApplicationsPanelLaunchHandler _launchHandler;
    private readonly IOpenFileDialogAdapterFactory _openFileDialogFactory;
    private readonly ApplicationsHandlerSyncHelper? _handlerSyncHelper;
    private IApplicationsPanelCommandView? _view;

    public ApplicationsPanelCommandCoordinator(
        ApplicationsCrudOrchestrator crudOrchestrator,
        ApplicationsPanelLaunchHandler launchHandler,
        IOpenFileDialogAdapterFactory openFileDialogFactory,
        ApplicationsHandlerSyncHelper? handlerSyncHelper = null)
    {
        _crudOrchestrator = crudOrchestrator;
        _launchHandler = launchHandler;
        _openFileDialogFactory = openFileDialogFactory;
        _handlerSyncHelper = handlerSyncHelper;
    }

    public void Initialize(IApplicationsPanelCommandView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (_view != null)
            throw new InvalidOperationException("ApplicationsPanelCommandCoordinator is already initialized.");

        _view = view;
    }

    public void HandleAdd()
    {
        EnsureInitialized();
        _crudOrchestrator.OpenAddDialog();
    }

    public void HandleEditSelected()
    {
        EnsureInitialized();
        _crudOrchestrator.EditSelected();
    }

    public Task HandleDeleteSelected()
    {
        EnsureInitialized();
        return _crudOrchestrator.RemoveSelected();
    }

    public void HandleManageHandlers()
    {
        var view = EnsureInitialized();
        _handlerSyncHelper?.OpenAssociationsDialog(
            view.GetOwner(),
            view.SaveAndRefresh);
    }

    public void HandleLaunchSelected()
    {
        var view = EnsureInitialized();
        var app = view.GetSelectedApp();
        if (app != null)
            _launchHandler.LaunchApp(app, null, view.GetOwner());
    }

    public void HandleRunAs()
    {
        var view = EnsureInitialized();
        using var dlgAdapter = _openFileDialogFactory.Create();
        var dlg = dlgAdapter.Dialog;
        dlg.Title = "Run As - Select File";
        dlg.Filter = "Programs (*.exe;*.cmd;*.bat;*.com;*.lnk)|*.exe;*.cmd;*.bat;*.com;*.lnk|All Files (*.*)|*.*";
        if (dlgAdapter.ShowDialog(view.GetOwner()) != DialogResult.OK)
            return;

        _launchHandler.TriggerRunAs(dlg.FileName);
    }

    private IApplicationsPanelCommandView EnsureInitialized()
    {
        if (_view == null)
            throw new InvalidOperationException("ApplicationsPanelCommandCoordinator must be initialized before use.");

        return _view;
    }
}
