using System.ComponentModel;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.UI;
using RunFence.UI.Forms;

namespace RunFence.Apps.UI.Forms;

public partial class ApplicationsPanel : DataPanel, IApplicationsPanelContext, IApplicationsPanelState, IWizardRequestSource, IApplicationsPanelCommandView, IApplicationsPanelRefreshView
{
    private readonly AppGridDragDropHandler _dragDropHandler;

    /// <summary>Tag type used on group header rows to distinguish them from app rows.</summary>
    public record struct ConfigGroupHeaderTag(string? ConfigPath);

    private readonly ApplicationsPanelLaunchHandler _launchHandler;
    private readonly ApplicationsPanelSaveHelper _saveHelper;
    private readonly ApplicationsCrudOrchestrator _crudHandler;
    private readonly ApplicationsGridPopulator _gridPopulator;
    private readonly AppContextMenuOrchestrator _contextMenuHandler;
    private readonly ApplicationsHandlerSyncHelper? _handlerSyncHelper;
    private readonly ApplicationsPanelCommandCoordinator _commandCoordinator;
    private readonly ApplicationsPanelRefreshCoordinator _refreshCoordinator;
    private DropFilesInterceptor? _dropFilesInterceptor;

    // IApplicationsPanelContext explicit implementations
    CredentialStore IApplicationsPanelContext.CredentialStore => CredentialStore;
    DataGridView IApplicationsPanelContext.Grid => _grid;
    void IApplicationsPanelContext.ShowModalDialog(Form dialog) => ShowModalDialog(dialog);
    void IApplicationsPanelContext.LaunchApp(AppEntry app, string? launcherArguments)
        => LaunchApp(app, launcherArguments);
    
    // IApplicationMutationContext explicit implementations
    AppDatabase IApplicationMutationContext.Database => Database;

    void IApplicationMutationContext.SaveAndRefresh(string? selectAppId, int fallbackIndex, bool targetedSave)
        => SaveAndRefresh(selectAppId, fallbackIndex, targetedSave);

    void IApplicationMutationContext.RefreshAfterInMemoryMutation(string? selectAppId, int fallbackIndex)
        => RefreshAfterInMemoryMutation(selectAppId, fallbackIndex);

    // IApplicationsPanelState explicit implementations
    AppDatabase IApplicationsPanelState.Database => Database;
    CredentialStore IApplicationsPanelState.CredentialStore => CredentialStore;
    bool IApplicationsPanelState.IsSortActive => IsSortActive;
    int IApplicationsPanelState.SortColumnIndex => SortColumnIndex;

    public event Action? DataChanged;
    public event Action? EnforcementRequested;
    public event Action<string>? AccountNavigationRequested;

    /// <summary>
    /// Fired when the wizard button is clicked. The parent subscribes and opens the wizard.
    /// Set <see cref="WizardButtonEnabled"/> to true after subscribing to show the button.
    /// </summary>
    public event Action<IWin32Window>? WizardRequested;

    /// <summary>
    /// Shows or hides the wizard toolbar button. Set to true after subscribing to <see cref="WizardRequested"/>.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool WizardButtonEnabled
    {
        get => _wizardButton.Visible;
        set => _wizardButton.Visible = value;
    }

    public ApplicationsPanel(
        IModalCoordinator modalCoordinator,
        ApplicationsCrudOrchestrator crudOrchestrator,
        ApplicationsGridPopulator gridPopulator,
        AppGridDragDropHandler dragDropHandler,
        ApplicationsPanelLaunchHandler launchHandler,
        ApplicationsPanelSaveHelper saveHelper,
        AppContextMenuOrchestrator contextMenuOrchestrator,
        ApplicationsPanelCommandCoordinator commandCoordinator,
        ApplicationsPanelRefreshCoordinator refreshCoordinator,
        ApplicationsHandlerSyncHelper? handlerSyncHelper = null)
        : base(modalCoordinator)
    {
        _crudHandler = crudOrchestrator;
        _gridPopulator = gridPopulator;
        _dragDropHandler = dragDropHandler;
        _launchHandler = launchHandler;
        _saveHelper = saveHelper;
        _contextMenuHandler = contextMenuOrchestrator;
        _commandCoordinator = commandCoordinator;
        _refreshCoordinator = refreshCoordinator;
        _handlerSyncHelper = handlerSyncHelper;
        InitializeComponent();
        BuildDynamicContent();
    }

    private void BuildDynamicContent()
    {
        _gridPopulator.Initialize(_grid, this, (items, key) => SortByActiveColumn(items, key));
        _crudHandler.Initialize(this);
        _dragDropHandler.Initialize(_grid, this, appId => SaveAndRefresh(appId));
        _commandCoordinator.Initialize(this);
        _refreshCoordinator.Initialize(this);
        _grid.HandleCreated += (_, _) =>
        {
            var old = _dropFilesInterceptor;
            _dropFilesInterceptor = new DropFilesInterceptor(_grid.Handle, OnGridFileDrop);
            old?.Dispose();
        };
        Disposed += (_, _) => _dropFilesInterceptor?.Dispose();
        DataGridViewGroupHeaderHelper.SuppressGroupHeaderTooltips<ConfigGroupHeaderTag>(_grid);
        EnableThreeStateSorting(_grid, RefreshGrid, sectioned: true);
        _addButton.Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22), 42);
        _wizardButton.Image = UiIconFactory.CreateToolbarIcon("\u2728", Color.FromArgb(0x33, 0x66, 0x99), 42);
        _wizardButton.Visible = false;
        _wizardButton.Click += (_, _) => WizardRequested?.Invoke(this);
        _editButton.Image = UiIconFactory.CreateToolbarIcon("\u270E", Color.FromArgb(0x33, 0x66, 0x99), 42);
        _removeButton.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33), 42);
        _launchButton.Image = UiIconFactory.CreateToolbarIcon("\u25B6", Color.FromArgb(0x22, 0x8B, 0x22), 42);
        _refreshButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F6E1", Color.FromArgb(0x33, 0x66, 0x99), 42);
        _ctxEdit.Image = UiIconFactory.CreateToolbarIcon("\u270E", Color.FromArgb(0x33, 0x66, 0x99), 16);
        _ctxRemove.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33), 16);
        _ctxLaunch.Image = UiIconFactory.CreateToolbarIcon("\u25B6", Color.FromArgb(0x22, 0x8B, 0x22), 16);
        _ctxRunAs.Image = UiIconFactory.CreateToolbarIcon("\u26A1", Color.FromArgb(0xCC, 0x77, 0x00), 16);
        _ctxOpenInFolderBrowser.Image = UiIconFactory.CreateToolbarIcon("\U0001F4C2", Color.FromArgb(0xCC, 0x88, 0x22), 16);
        _hdrAdd.Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22), 16);
        _associationsButton.Image = UiIconFactory.CreateToolbarIcon("\u21C4", Color.FromArgb(0x33, 0x66, 0x99), 42);
        _associationsButton.Visible = _handlerSyncHelper != null;
        _runAsButton.Image = UiIconFactory.CreateToolbarIcon("\u26A1", Color.FromArgb(0xCC, 0x77, 0x00), 42);
        _contextMenuHandler.AccountNavigationRequested += accountSid => AccountNavigationRequested?.Invoke(accountSid);
        _contextMenuHandler.DataSaveAndRefreshRequested += OnContextMenuDataSaveAndRefresh;
    }

    protected override void OnDataSet()
    {
        RefreshGrid();
    }

    protected override void UpdateButtonState()
    {
        var hasAppSelected = _grid.SelectedRows.Count > 0 && _grid.SelectedRows[0].Tag is AppEntry;
        _editButton.Enabled = hasAppSelected;
        _removeButton.Enabled = hasAppSelected;
        _launchButton.Enabled = hasAppSelected;
    }

    private void OnGridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
            return;
        if (_grid.Rows[e.RowIndex].Tag is not AppEntry)
            return;
        OnEditClick(sender, e);
    }

    private void OnGridCellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e is { Button: MouseButtons.Right, RowIndex: >= 0 } && _grid.Rows[e.RowIndex].Tag is ConfigGroupHeaderTag)
            GridSetupHelper.HandleRightClickRowSelect(_grid, e, _headerContextMenu);
        else
            GridSetupHelper.HandleRightClickRowSelect(_grid, e, _contextMenu);
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (_grid.SelectedRows.Count == 0)
            return;

        switch (e.KeyCode)
        {
            case Keys.Delete:
                OnRemoveClick(sender, e);
                break;
            case Keys.F2:
                OnEditClick(sender, e);
                break;
            case Keys.Enter:
                e.Handled = true;
                e.SuppressKeyPress = true;
                OnLaunchClick(sender, e);
                break;
            case Keys.Apps:
                GridSetupHelper.ShowContextMenuAtRow(_grid, _contextMenu);
                e.Handled = true;
                break;
            case Keys.F10 when e.Shift:
                GridSetupHelper.ShowContextMenuAtRow(_grid, _contextMenu);
                e.Handled = true;
                break;
        }
    }

    private void OnGridMouseDown(object? sender, MouseEventArgs e)
        => _dragDropHandler.HandleMouseDown(e);

    private void OnGridMouseMove(object? sender, MouseEventArgs e)
        => _dragDropHandler.HandleMouseMove(e);

    private void OnGridMouseUp(object? sender, MouseEventArgs e)
        => _dragDropHandler.HandleMouseUp(e);

    private void OnGridFileDrop(string[] paths)
        => _crudHandler.OpenAddDialogBatch(paths);

    private void RefreshGrid()
    {
        _ = _refreshCoordinator.RefreshAsync(CancellationToken.None);
    }

    public void SelectAppById(string? appId)
        => _gridPopulator.SelectAppById(appId);

    public void EditAppById(string appId, AppEditDialogOptions? options = null)
    {
        var app = Database.Apps.FirstOrDefault(a => a.Id == appId);
        if (app != null)
            _crudHandler.EditApp(app, options);
    }

    private void SelectFirstRow()
        => _gridPopulator.SelectFirstRow();

    private void SelectRowByIndex(int index)
        => _gridPopulator.SelectRowByIndex(index);

    public void OpenAddDialogForAccount(string accountSid)
        => _crudHandler.OpenAddDialog(initialAccountSid: accountSid);

    private void OnAddClick(object? sender, EventArgs e)
        => _commandCoordinator.HandleAdd();

    private void OnEditClick(object? sender, EventArgs e)
        => _commandCoordinator.HandleEditSelected();

    private async void OnRemoveClick(object? sender, EventArgs e)
        => await _commandCoordinator.HandleDeleteSelected();

    private void OnLaunchClick(object? sender, EventArgs e)
        => _commandCoordinator.HandleLaunchSelected();

    private void OnRunAsClick(object? sender, EventArgs e)
        => _commandCoordinator.HandleRunAs();

    private void OnAssociationsClick(object? sender, EventArgs e)
        => _commandCoordinator.HandleManageHandlers();

    private void OnReapplyClick(object? sender, EventArgs e)
    {
        EnforcementRequested?.Invoke();
    }

    private void OnContextMenuDataSaveAndRefresh()
    {
        _ = _refreshCoordinator.RefreshAsync(CancellationToken.None);
        DataChanged?.Invoke();
    }

    private void OnCopyLauncherPathClick(object? sender, EventArgs e)
    {
        if (GetSelectedApp() is { } app)
            _contextMenuHandler.CopyLauncherPath(app);
    }

    private void OnSaveShortcutClick(object? sender, EventArgs e)
    {
        if (GetSelectedApp() is { } app)
            _contextMenuHandler.SaveShortcut(app, FindForm());
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not AppEntry app)
        {
            e.Cancel = true;
            return;
        }

        _ctxGoToAccount.Enabled = CredentialStore.Credentials.Any(c => string.Equals(c.Sid, app.AccountSid, StringComparison.OrdinalIgnoreCase));

        _ctxCopyPath.Text = app.IsFolder ? "Copy Folder Path" : "Copy Path";
        _ctxRunAs.Visible = !app.IsFolder && !app.IsUrlScheme;
        _ctxOpenDir.Visible = !app.IsFolder;
        _ctxOpenFolder.Visible = app.IsFolder;

        var isUrl = !string.IsNullOrEmpty(app.ExePath) && PathHelper.IsUrlScheme(app.ExePath);
        if (!app.IsFolder && !isUrl)
        {
            _ctxOpenInFolderBrowser.Text = $"Open in Folder Browser as {_gridPopulator.ResolveAppAccountName(app)}";
            _ctxOpenInFolderBrowser.Visible = true;
            var cred = CredentialStore.Credentials.FirstOrDefault(c =>
                string.Equals(c.Sid, app.AccountSid, StringComparison.OrdinalIgnoreCase));
            _ctxOpenInFolderBrowser.Enabled = SidResolutionHelper.CanLaunchWithoutPassword(app.AccountSid)
                                              || cred?.EncryptedPassword.Length > 0;
        }
        else
            _ctxOpenInFolderBrowser.Visible = false;

        if (_handlerSyncHelper != null && app is { IsFolder: false, IsUrlScheme: false })
        {
            _ctxSetDefaultBrowser.Visible = true;
            _ctxSetDefaultBrowser.Checked = _handlerSyncHelper.IsDefaultBrowser(app.Id);
        }
        else
            _ctxSetDefaultBrowser.Visible = false;
    }

    private void OnGoToAccountClick(object? sender, EventArgs e)
    {
        if (GetSelectedApp() is { } app)
            _contextMenuHandler.GoToAccount(app);
    }

    private void OnOpenInFolderBrowserClick(object? sender, EventArgs e)
    {
        if (GetSelectedApp() is { } app)
            _contextMenuHandler.OpenInFolderBrowser(app, FindForm());
    }

    private void OnOpenFolderClick(object? sender, EventArgs e)
    {
        if (GetSelectedApp() is { } app)
            _contextMenuHandler.OpenFolder(app);
    }

    private void OnContextMenuRunAsClick(object? sender, EventArgs e)
    {
        if (GetSelectedApp() is { IsFolder: false, IsUrlScheme: false } app)
            _launchHandler.TriggerRunAs(app.ExePath);
    }

    private void OnCopyPathClick(object? sender, EventArgs e)
    {
        if (GetSelectedApp() is { } app)
            _contextMenuHandler.CopyPath(app);
    }

    private void OnOpenDirClick(object? sender, EventArgs e)
    {
        if (GetSelectedApp() is { } app)
            _contextMenuHandler.OpenDir(app);
    }

    private void OnSetDefaultBrowserClick(object? sender, EventArgs e)
    {
        if (GetSelectedApp() is { } app)
            _contextMenuHandler.SetDefaultBrowser(app);
    }

    private AppEntry? GetSelectedApp()
        => _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Tag as AppEntry : null;

    public void LaunchApp(AppEntry app, string? launcherArguments)
        => _launchHandler.LaunchApp(app, launcherArguments, FindForm());

    private void SaveAndRefresh(string? selectAppId = null, int fallbackIndex = -1, bool targetedSave = false)
        => _ = _refreshCoordinator.SaveRefreshAndReselectAsync(
            selectAppId,
            fallbackIndex,
            targetedSave,
            CancellationToken.None);

    private void RefreshAfterInMemoryMutation(string? selectAppId = null, int fallbackIndex = -1)
        => _refreshCoordinator.RefreshAfterInMemoryMutation(selectAppId, fallbackIndex);

    AppEntry? IApplicationsPanelCommandView.GetSelectedApp() => GetSelectedApp();
    IWin32Window IApplicationsPanelCommandView.GetOwner() => this;
    void IApplicationsPanelCommandView.SaveAndRefresh() => SaveAndRefresh();

    void IApplicationsPanelRefreshView.SetIsRefreshing(bool isRefreshing) => IsRefreshing = isRefreshing;
    void IApplicationsPanelRefreshView.ReapplyGlyphIfActive() => ReapplyGlyphIfActive(_grid);
    void IApplicationsPanelRefreshView.UpdateButtonState() => UpdateButtonState();
    void IApplicationsPanelRefreshView.SelectAppById(string? appId) => SelectAppById(appId);
    void IApplicationsPanelRefreshView.SelectRowByIndex(int rowIndex) => SelectRowByIndex(rowIndex);
    void IApplicationsPanelRefreshView.SelectFirstRow() => SelectFirstRow();
    void IApplicationsPanelRefreshView.PublishDataChanged() => DataChanged?.Invoke();
}
