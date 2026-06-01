using System.ComponentModel;
using RunFence.Core;
using RunFence.Core.Infrastructure;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.RunAs;
using RunFence.UI;
using RunFence.UI.Forms;

namespace RunFence.Account.UI.Forms;

public partial class AccountsPanel : DataPanel,
    IAccountsPanelContext, IAccountsPanelDataContext, IAccountsPanelOperationContext,
    IGridSortState, IAccountGridCallbacks,
    IAccountsPanelLifecycleView, IAccountsPanelSelectionSaveView
{
    private readonly SessionPersistenceHelper _persistenceHelper;
    private readonly OperationGuard _operationGuard;
    private readonly IRunAsFlowHandler _runAsFlowHandler;
    private readonly AccountContainerOrchestrator _containerHandler;
    private readonly IAccountBulkScanHandler? _bulkScanHandler;
    private readonly AccountsPanelTimerCoordinator _timerCoordinator;
    private readonly AccountSidMigrationLauncher _migrationLauncher;
    private readonly AccountProcessDisplayManager _processDisplayManager;
    private readonly AccountsPanelGridInteraction _gridInteraction;
    private readonly AccountListPresenter _accountListPresenter;
    private readonly AccountsPanelRefreshOrchestrator _refreshOrchestrator;
    private readonly AccountContextMenuOrchestrator _contextMenuOrchestrator;
    private readonly AccountsPanelCredentialHandler _credentialHandler;
    private readonly AccountCreationOrchestrator _creationHandler;
    private readonly AccountDeletionOrchestrator _deletionOrchestrator;
    private readonly AccountPanelActions _panelActions;
    private readonly AccountImportUIHandler _importUiHandler;
    private readonly AccountGridEditHandler _gridEditHandler;
    private readonly AccountGridTypeAheadHandler _typeAheadHandler;
    private readonly IOpenFileDialogAdapterFactory _openFileDialogFactory;
    private readonly AccountsPanelLifecycleCoordinator _lifecycleCoordinator;
    private readonly AccountsPanelSelectionSaveCoordinator _selectionSaveCoordinator;

    private long _processRefreshGeneration;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool RenameInProgress
    {
        get => _gridEditHandler.RenameInProgress;
        set => _gridEditHandler.RenameInProgress = value;
    }

    public event Action? DataChanged;
    public event Action<string>? AppNavigationRequested;
    public event Action<string>? NewAppRequested;

    AppDatabase IAccountsPanelDataContext.Database => Database;
    CredentialStore IAccountsPanelDataContext.CredentialStore => CredentialStore;
    bool IAccountsPanelDataContext.IsRefreshing => IsRefreshing;
    bool IAccountsPanelLifecycleView.IsRefreshing => IsRefreshing;

    Control IAccountsPanelOperationContext.OwnerControl => this;
    OperationGuard IAccountsPanelOperationContext.OperationGuard => _operationGuard;
    bool IAccountsPanelOperationContext.RenameInProgress { set => RenameInProgress = value; }
    DialogResult IAccountsPanelOperationContext.ShowModal(Form dialog) => ShowModalDialog(dialog);
    void IAccountsPanelOperationContext.SaveAndRefresh(Guid? selectCredentialId, int fallbackIndex)
        => _ = SaveAndRefreshAsync(selectCredentialId, fallbackIndex);
    void IAccountsPanelOperationContext.RefreshAndNotifyDataChanged(Guid? selectCredentialId, int fallbackIndex)
        => _ = RefreshAndNotifyDataChangedAsync(selectCredentialId, fallbackIndex);
    void IAccountsPanelOperationContext.UpdateStatus(string text) => _statusLabel.Text = text;
    void IAccountsPanelOperationContext.UpdateButtonState() => UpdateButtonState();
    void IAccountsPanelOperationContext.SetControlsEnabled(bool enabled) => SetControlsEnabled(enabled);
    void IAccountsPanelOperationContext.SaveLastPrefsPath(string path) => SaveLastPrefsPath(path);
    void IAccountsPanelOperationContext.RefreshGrid() => _ = RefreshGridCoreAsync();

    long IAccountsPanelOperationContext.BeginProcessRefreshGeneration()
        => Interlocked.Increment(ref _processRefreshGeneration);

    void IAccountsPanelOperationContext.TriggerProcessRefresh(long generation, int delayMs)
    {
        if (generation != Volatile.Read(ref _processRefreshGeneration))
            return;

        _processDisplayManager.TriggerDelayedRefresh(delayMs);
    }

    bool IGridSortState.IsSortActive => IsSortActive;
    int IGridSortState.SortColumnIndex => SortColumnIndex;
    bool IGridSortState.SortDescending => SortDirection == SortOrder.Descending;

    void IAccountGridCallbacks.ReapplyGlyph() => ReapplyGlyphIfActive(_grid);
    void IAccountGridCallbacks.SelectFirstRow() => GridSetupHelper.SelectFirstRow(_grid);
    void IAccountGridCallbacks.UpdateButtonState() => UpdateButtonState();
    void IAccountGridCallbacks.SetIsRefreshing() => IsRefreshing = true;
    void IAccountGridCallbacks.ClearIsRefreshing() => IsRefreshing = false;
    void IAccountGridCallbacks.UpdateStatus(string text) => _statusLabel.Text = text;
    void IAccountGridCallbacks.ClearStatus() => _statusLabel.Text = "";

    bool IAccountsPanelLifecycleView.IsSortActive => IsSortActive;
    bool IAccountsPanelLifecycleView.IsParentFormVisible() => IsParentFormVisible();
    Task IAccountsPanelLifecycleView.InitialRefreshAsync() => _refreshOrchestrator.InitialRefreshAsync();
    void IAccountsPanelLifecycleView.ShowSidMigrationWarning() => ShowSidMigrationWarning();

    SessionContext IAccountsPanelSelectionSaveView.Session => Session;
    AppDatabase IAccountsPanelSelectionSaveView.Database => Database;
    CredentialStore IAccountsPanelSelectionSaveView.CredentialStore => CredentialStore;
    void IAccountsPanelSelectionSaveView.SelectBySid(string sid) => SelectBySid(sid);
    void IAccountsPanelSelectionSaveView.RaiseDataChanged() => DataChanged?.Invoke();

    public AccountsPanel(
        IModalCoordinator modalCoordinator,
        SessionPersistenceHelper persistenceHelper,
        AccountContainerOrchestrator containerHandler,
        AccountsPanelRefreshOrchestrator refreshOrchestrator,
        AccountContextMenuOrchestrator contextMenuOrchestrator,
        AccountsPanelCredentialHandler credentialHandler,
        AccountCreationOrchestrator creationHandler,
        AccountDeletionOrchestrator deletionOrchestrator,
        AccountPanelActions panelActions,
        OperationGuard operationGuard,
        AccountsPanelTimerCoordinator timerCoordinator,
        AccountSidMigrationLauncher migrationLauncher,
        AccountsPanelGridInteraction gridInteraction,
        AccountProcessDisplayManager processDisplayManager,
        AccountImportUIHandler importUiHandler,
        AccountGridEditHandler gridEditHandler,
        AccountGridTypeAheadHandler typeAheadHandler,
        AccountListPresenter accountListPresenter,
        IOpenFileDialogAdapterFactory openFileDialogFactory,
        AccountsPanelLifecycleCoordinator lifecycleCoordinator,
        AccountsPanelSelectionSaveCoordinator selectionSaveCoordinator,
        IRunAsFlowHandler runAsFlowHandler,
        IAccountBulkScanHandler? bulkScanHandler = null,
        IWizardLauncher? wizardLauncher = null)
        : base(modalCoordinator)
    {
        _persistenceHelper = persistenceHelper;
        _runAsFlowHandler = runAsFlowHandler;
        _containerHandler = containerHandler;
        _refreshOrchestrator = refreshOrchestrator;
        _contextMenuOrchestrator = contextMenuOrchestrator;
        _credentialHandler = credentialHandler;
        _creationHandler = creationHandler;
        _deletionOrchestrator = deletionOrchestrator;
        _panelActions = panelActions;
        _operationGuard = operationGuard;
        _timerCoordinator = timerCoordinator;
        _migrationLauncher = migrationLauncher;
        _gridInteraction = gridInteraction;
        _processDisplayManager = processDisplayManager;
        _importUiHandler = importUiHandler;
        _gridEditHandler = gridEditHandler;
        _typeAheadHandler = typeAheadHandler;
        _accountListPresenter = accountListPresenter;
        _openFileDialogFactory = openFileDialogFactory;
        _lifecycleCoordinator = lifecycleCoordinator;
        _selectionSaveCoordinator = selectionSaveCoordinator;
        _bulkScanHandler = bulkScanHandler;

        InitializeComponent();

        DataGridViewGroupHeaderHelper.SuppressGroupHeaderTooltips<AccountGroupHeader>(_grid);
        _grid.CellMouseEnter += OnGridCellMouseEnter;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.Empty;
        EnableThreeStateSorting(_grid, () => _ = RefreshGridCoreAsync(), sectioned: true);

        BuildDynamicContent(wizardLauncher);
    }

    private void BuildDynamicContent(IWizardLauncher? wizardLauncher)
    {
        _refreshButton.Image = UiIconFactory.CreateToolbarIcon("\u21BB", Color.FromArgb(0x33, 0x66, 0x99), 48);
        _addButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F511", Color.FromArgb(0x22, 0x8B, 0x22), 42);
        _createUserButton.Image = AccountGridHelper.CreateUserProfileIcon(42);
        _createContainerButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F4E6", Color.FromArgb(0x33, 0x66, 0xCC), 42);
        _createContainerButton.Visible = true;
        _openCmdButton.Image = UiIconFactory.CreateToolbarIcon(">", Color.FromArgb(0x33, 0x33, 0x33), 42);
        _openFolderBrowserButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F4C2", Color.FromArgb(0xCC, 0x88, 0x22), 42);
        _runAsButton.Image = UiIconFactory.CreateToolbarIcon("\u26A1", Color.FromArgb(0xCC, 0x77, 0x00), 42);
        _copyPasswordButton.Image = UiIconFactory.CreateToolbarIcon("\u26BF", Color.FromArgb(0x66, 0x66, 0x99), 42);
        _importButton.Image = UiIconFactory.CreateToolbarIcon("\u2193", Color.FromArgb(0x33, 0x66, 0x99), 42);
        _accountsButton.Image = UiIconFactory.CreateToolbarIcon("\u2699", Color.FromArgb(0x33, 0x66, 0x99), 42);
        _migrateSidsButton.Image = UiIconFactory.CreateToolbarIcon("\u21C4", Color.FromArgb(0x66, 0x66, 0x99), 42);
        _deleteProfilesButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F5D1", Color.FromArgb(0x99, 0x33, 0x33), 42);
        _aclManagerButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F4DC", Color.FromArgb(0xCC, 0x99, 0x00), 42);
        _scanAclsButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F50D", Color.FromArgb(0x22, 0x88, 0x44), 42);
        _scanAclsButton.Visible = _bulkScanHandler != null;
        _lowIntegrityAclManagerButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F4DC", Color.FromArgb(0x22, 0x88, 0x44), 42);
        _appContainersAclManagerButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F4DC", Color.FromArgb(0x33, 0x66, 0xCC), 42);
        _firewallButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F310", Color.FromArgb(0x22, 0x66, 0xCC), 42);
        _firewallButton.ToolTipText = "Internet Allowlist";
        _firewallButton.Visible = _contextMenuOrchestrator.IsFirewallAvailable;
        _wizardButton.Image = UiIconFactory.CreateToolbarIcon("\u2728", Color.FromArgb(0x33, 0x66, 0x99), 42);
        _wizardButton.Visible = wizardLauncher != null;
        if (wizardLauncher != null)
        {
            _wizardButton.Click += async (_, _) => await wizardLauncher.OpenWizardAsync(this);
            wizardLauncher.WizardCompleted += () => DataChanged?.Invoke();
        }

        _hdrAdd.Image = UiIconFactory.CreateToolbarIcon("\U0001F511", Color.FromArgb(0x22, 0x8B, 0x22), 16);
        _hdrCreateUser.Image = AccountGridHelper.CreateUserProfileIcon(16);
        _hdrCreateContainer.Image = UiIconFactory.CreateToolbarIcon("\U0001F4E6", Color.FromArgb(0x33, 0x66, 0xCC), 16);
        _hdrCreateContainer.Visible = true;

        _processDisplayManager.Initialize(_grid, this);
        _refreshOrchestrator.Initialize(_grid, this, this, _processDisplayManager);
        _contextMenuOrchestrator.Initialize(_grid, _contextMenu, this, _hdrCreateContainer, _processDisplayManager);
        _credentialHandler.Initialize(this, this);
        _creationHandler.Initialize(this);
        _panelActions.Initialize(_grid, this);
        _importUiHandler.Initialize(_grid, _importButton);
        _gridEditHandler.Initialize(_grid, this);
        _typeAheadHandler.Initialize(_grid);
        _lifecycleCoordinator.Initialize(this);
        _selectionSaveCoordinator.Initialize(this);

        _contextMenuOrchestrator.AppNavigationRequested += sid => AppNavigationRequested?.Invoke(sid);
        _contextMenuOrchestrator.NewAppRequested += sid => NewAppRequested?.Invoke(sid);
        _contextMenuOrchestrator.DataChangedAndRefresh += async () =>
        {
            DataChanged?.Invoke();
            await RefreshGridCoreAsync();
        };
        _contextMenuOrchestrator.ShowSystemInRunAsToggleRequested += async () =>
        {
            Database.ShowSystemInRunAs = !Database.ShowSystemInRunAs;
            _persistenceHelper.SaveConfig(Database, Session.PinDerivedKey, CredentialStore.ArgonSalt);
            DataChanged?.Invoke();
            await RefreshGridCoreAsync();
        };
        _openCmdButton.ButtonClick += OnOpenCmdButtonClick;
        _openCmdButton.DropDownOpening += OnOpenCmdButtonDropDownOpening;
        _openFolderBrowserButton.ButtonClick += OnOpenFolderBrowserButtonClick;
        _openFolderBrowserButton.DropDownOpening += OnOpenFolderBrowserButtonDropDownOpening;
        _aclManagerButton.Click += (_, _) => _contextMenuOrchestrator.OpenAclManager();
        _firewallButton.Click += (_, _) => _contextMenuOrchestrator.OpenFirewallAllowlist();

        _credentialHandler.CreateUserDialogRequested += (username, password) => OpenCreateUserDialog(username, password);
        _credentialHandler.DeleteUserRequested += (accountRow, selectedIndex) => _deletionOrchestrator.DeleteUser(accountRow, selectedIndex);
        _credentialHandler.SaveAndRefreshRequested += async (credId, fallbackIndex) => await SaveAndRefreshAsync(credId, fallbackIndex);

        _deletionOrchestrator.SaveAndRefreshRequested += async (credId, fallbackIndex) => await SaveAndRefreshAsync(credId, fallbackIndex);
        _deletionOrchestrator.StatusUpdateRequested += text => _statusLabel.Text = text;
        _deletionOrchestrator.OperationStarted += () => _operationGuard.Begin(this);
        _deletionOrchestrator.OperationEnded += () => _operationGuard.End(this);

        var items = _contextMenuOrchestrator.Items;
        items.FirewallAllowlist.Image = UiIconFactory.CreateToolbarIcon("\U0001F310", Color.FromArgb(0x22, 0x66, 0xCC), 16);
        items.EditAccount.Click += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } accountRow)
                _credentialHandler.EditAccount(accountRow, GetSelectedRowIndex());
        };
        items.EditCredential.Click += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } accountRow)
                _credentialHandler.EditCredential(accountRow);
        };
        items.RemoveCredential.Click += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } accountRow)
                _credentialHandler.RemoveCredential(accountRow, GetSelectedRowIndex());
        };
        items.DeleteUser.Click += OnDeleteUserClick;
        items.PinFolderBrowserToTray.Click += (_, _) => _contextMenuOrchestrator.ToggleFolderBrowserTray();
        items.PinDiscoveryToTray.Click += (_, _) => _contextMenuOrchestrator.ToggleDiscoveryTray();
        items.PinTerminalToTray.Click += (_, _) => _contextMenuOrchestrator.ToggleTerminalTray();
        items.ManageAssociations.Click += (_, _) => _contextMenuOrchestrator.ToggleManageAssociations();
        items.ReceiveInjectedInput.Click += (_, _) => _contextMenuOrchestrator.ToggleReceiveInjectedInput();
        items.CopySid.Click += (_, _) =>
        {
            var sid = GetSelectedSid();
            if (sid != null)
                _panelActions.CopySid(sid);
        };
        items.CopyProfilePath.Click += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } accountRow)
                _panelActions.CopyProfilePath(accountRow.Sid);
        };
        items.OpenProfileFolder.Click += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } accountRow)
                _panelActions.OpenProfileFolder(accountRow.Sid);
        };
        items.CopyPassword.Click += async (_, _) =>
        {
            if (GetSelectedAccountRow() is { } accountRow)
                await _credentialHandler.CopyPasswordAsync(accountRow);
        };
        items.TypePassword.Click += async (_, _) =>
        {
            if (GetSelectedAccountRow() is { } accountRow)
                await _credentialHandler.TypePasswordAsync(accountRow);
        };
        items.RotatePassword.Click += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } accountRow)
                _credentialHandler.RotatePassword(accountRow);
        };
        items.SetEmptyPassword.Click += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } accountRow)
                _credentialHandler.SetEmptyPassword(accountRow);
        };
        _contextMenuOrchestrator.EditCredentialRequested += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } accountRow)
                _credentialHandler.EditCredential(accountRow);
        };

        _grid.KeyPress += OnGridKeyPress;
        _grid.CellBeginEdit += (_, e) => _gridEditHandler.HandleCellBeginEdit(e);
        _grid.CellEndEdit += (_, e) => _gridEditHandler.HandleCellEndEdit(e);
        _grid.CurrentCellDirtyStateChanged += (_, _) => _gridEditHandler.HandleDirtyStateChanged();
        _grid.CellValueChanged += (_, e) => _gridEditHandler.HandleCellValueChanged(e);
        _grid.CellValidating += (_, e) => _gridEditHandler.HandleCellValidating(e);
        _grid.EditingControlShowing += (_, e) => _gridEditHandler.HandleEditingControlShowing(e);
        _grid.CellPainting += OnGridCellPainting;
    }

    public void RegisterContextHelp(ContextHelpForm host)
        => host.SetContextHelp(_appContainersAclManagerButton, ContextHelpTextCatalog.Account_AppContainersAclManager);

    private void OnCreateContainerClick(object? sender, EventArgs e)
    {
        _containerHandler.CreateContainer(FindForm(), () =>
        {
            DataChanged?.Invoke();
            _ = RefreshGridCoreAsync();
        });
    }

    private async void OnScanAclsClick(object? sender, EventArgs e)
    {
        if (_bulkScanHandler == null)
            return;

        await _bulkScanHandler.ScanAcls(this, new ScanProgressReporter(_scanAclsButton, _statusLabel));
    }

    private void OnLowIntegrityAclManagerClick(object? sender, EventArgs e)
        => _containerHandler.OpenLowIntegrityAclManager(FindForm());

    private void OnAppContainersAclManagerClick(object? sender, EventArgs e)
        => _containerHandler.OpenAppContainersAclManager(FindForm());

    protected override void OnDataSet()
        => _lifecycleCoordinator.Initialize();

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        _lifecycleCoordinator.OnParentChanged(Parent);
    }

    public override void RefreshOnActivation()
        => _ = _lifecycleCoordinator.RefreshOnActivationAsync();

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        _lifecycleCoordinator.OnVisibleChanged(Visible);
    }

    private bool IsParentFormVisible()
    {
        var form = FindForm();
        return form is { Visible: true } && form.WindowState != FormWindowState.Minimized;
    }

    private void OnMigrateSidsClick(object? sender, EventArgs e)
    {
        _operationGuard.Begin(this);
        try
        {
            _migrationLauncher.LaunchMigrationDialog(FindForm(), () =>
            {
                DataChanged?.Invoke();
                _ = RefreshGridCoreAsync();
                var oldImage = _migrateSidsButton.Image;
                _migrateSidsButton.Image = UiIconFactory.CreateToolbarIcon("\u21C4", Color.FromArgb(0x66, 0x66, 0x99), 42);
                _migrateSidsButton.ToolTipText = "Migrate SIDs...";
                oldImage?.Dispose();
            });
        }
        finally
        {
            _operationGuard.End(this);
        }
    }

    private void OnDeleteProfilesClick(object? sender, EventArgs e)
    {
        _operationGuard.Begin(this);
        try
        {
            _migrationLauncher.LaunchOrphanedProfilesDialog(FindForm());
        }
        finally
        {
            _operationGuard.End(this);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_grid.Focused && _grid.SelectedRows.Count > 0)
        {
            var selectedRow = _grid.SelectedRows[0];
            if (_gridInteraction.HandleCmdKey(keyData, selectedRow))
            {
                UpdateButtonState();
                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    public Task RefreshGridAsync(CancellationToken cancellationToken = default)
        => RefreshGridCoreAsync(null, cancellationToken);

    private Task RefreshGridCoreAsync(Action? afterPopulate = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var generation = _accountListPresenter.NextGeneration();
        return _refreshOrchestrator.RefreshGridAsync(() =>
        {
            if (!_accountListPresenter.IsCurrent(generation))
                return;

            afterPopulate?.Invoke();
        }, cancellationToken);
    }

    protected override void UpdateButtonState()
    {
        var hasSelection = _grid.SelectedRows.Count > 0;
        var gridRow = hasSelection ? _grid.SelectedRows[0].Tag as IAccountGridRow : null;
        var accountRow = gridRow as AccountRow;
        var containerRow = gridRow as ContainerRow;

        var hasAccountRow = accountRow != null;
        var isUnavailable = accountRow?.IsUnavailable == true;
        var canLaunch = accountRow?.CanLaunch == true;

        _openCmdButton.Enabled = canLaunch || containerRow != null;
        _openFolderBrowserButton.Enabled = canLaunch || containerRow != null;
        _aclManagerButton.Enabled =
            (accountRow != null && !isUnavailable && !SidResolutionHelper.IsSystemSid(accountRow.Sid)) ||
            (containerRow != null && !string.IsNullOrEmpty(containerRow.ContainerSid));
        _firewallButton.Enabled = hasAccountRow && !isUnavailable && !string.IsNullOrEmpty(accountRow?.Sid)
            && !SidResolutionHelper.IsSystemSid(accountRow.Sid);

        var useWindowsTerminal = accountRow != null && _contextMenuOrchestrator.IsWindowsTerminal(accountRow.Sid);
        _openCmdButton.ToolTipText = containerRow != null
            ? "Open CMD"
            : useWindowsTerminal
                ? "Open Terminal (primary click keeps Shift full-privilege behavior; use the drop-down for explicit privilege levels)"
                : "Open CMD (primary click keeps Shift full-privilege behavior; use the drop-down for explicit privilege levels)";
        _openFolderBrowserButton.ToolTipText = containerRow != null
            ? "Open Folder Browser"
            : "Open Folder Browser (primary click keeps Shift full-privilege behavior; use the drop-down for explicit privilege levels)";

        if (_grid.Columns[AccountGridColumns.Import]?.Visible == true)
            _importButton.Enabled = true;
    }

    private async void OnOpenCmdButtonClick(object? sender, EventArgs e)
        => await _contextMenuOrchestrator.OpenCmdWithTerminalLaunchRefreshAsync();

    private void OnOpenCmdButtonDropDownOpening(object? sender, EventArgs e)
        => PopulatePrivilegeLaunchMenu(
            _openCmdButton,
            "Open CMD",
            privilegeLevel => _contextMenuOrchestrator.OpenCmdWithTerminalLaunchRefreshAsync(privilegeLevel));

    private void OnOpenFolderBrowserButtonClick(object? sender, EventArgs e)
        => _contextMenuOrchestrator.OpenFolderBrowser();

    private void OnOpenFolderBrowserButtonDropDownOpening(object? sender, EventArgs e)
        => PopulatePrivilegeLaunchMenu(
            _openFolderBrowserButton,
            "Open Folder Browser",
            privilegeLevel =>
            {
                _contextMenuOrchestrator.OpenFolderBrowser(privilegeLevel);
                return Task.CompletedTask;
            });

    private void PopulatePrivilegeLaunchMenu(
        ToolStripSplitButton button,
        string defaultText,
        Func<PrivilegeLevel, Task> launchWithPrivilegeLevel)
    {
        ResetDropDownItems(button);

        if (GetSelectedContainerRow() != null)
        {
            var defaultItem = new ToolStripMenuItem(defaultText);
            defaultItem.Click += (_, _) => button.PerformButtonClick();
            button.DropDownItems.Add(defaultItem);
            return;
        }

        if (GetSelectedAccountRow() == null)
            return;

        foreach (var level in PrivilegeLevelComboHelper.OrderedModes)
        {
            var capturedLevel = level;
            var item = new ToolStripMenuItem(PrivilegeLevelComboHelper.GetDisplayText(level));
            item.Click += async (_, _) => await launchWithPrivilegeLevel(capturedLevel);
            button.DropDownItems.Add(item);
        }
    }

    private static void ResetDropDownItems(ToolStripSplitButton button)
    {
        for (var i = button.DropDownItems.Count - 1; i >= 0; i--)
        {
            var item = button.DropDownItems[i];
            button.DropDownItems.RemoveAt(i);
            item.Dispose();
        }
    }

    private void OnGridCellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
            return;

        var row = _grid.Rows[e.RowIndex];
        var colName = _grid.Columns[e.ColumnIndex].Name;

        switch (_gridInteraction.HandleCellContentClick(row, colName))
        {
            case GridClickAction.ToggleLogonAction toggleLogon:
                _gridEditHandler.HandleLogonToggle(row, toggleLogon.AccountRow);
                break;
            case GridClickAction.ToggleInternetAction toggleInternet:
                _gridEditHandler.HandleAllowInternetToggle(row, toggleInternet.AccountRow);
                break;
            case GridClickAction.ToggleContainerInternetAction toggleContainerInternet:
                HandleContainerInternetToggle(row, toggleContainerInternet.ContainerRow);
                break;
        }
    }

    private void HandleContainerInternetToggle(DataGridViewRow row, ContainerRow containerRow)
    {
        if (_operationGuard.IsInProgress)
            return;

        _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        var cell = (DataGridViewCheckBoxCell)row.Cells[AccountGridColumns.AllowInternet];
        var enable = cell.Value is true or CheckState.Checked;
        _containerHandler.ToggleContainerInternet(containerRow, enable, () =>
        {
            DataChanged?.Invoke();
            _ = RefreshGridCoreAsync();
        });
    }

    private void SelectByTag<T>(Func<T, bool> predicate) where T : class, IAccountGridRow
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is T value && predicate(value))
            {
                row.Selected = true;
                _grid.CurrentCell = row.Cells[AccountGridColumns.Account];
                return;
            }
        }
    }

    public void SelectBySid(string sid)
        => SelectByTag<AccountRow>(accountRow => string.Equals(accountRow.Sid, sid, StringComparison.OrdinalIgnoreCase));

    private void SelectCredentialById(Guid credentialId)
        => SelectByTag<AccountRow>(accountRow => accountRow.Credential?.Id == credentialId);

    private void OnGridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
            return;

        if (_gridInteraction.HandleDoubleClick(_grid.Rows[e.RowIndex]) == GridDoubleClickAction.OpenAclManager)
            _contextMenuOrchestrator.OpenAclManager();
    }

    private async void OnGridCellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        switch (_gridInteraction.HandleMouseClick(e, _grid, _processDisplayManager, IsSortActive))
        {
            case GridMouseClickResult.ExpandToggleResult expandToggle:
                await ToggleProcessRowsAsync(expandToggle.Sid);
                break;
            case GridMouseClickResult.RightClickHeaderResult:
                GridSetupHelper.HandleRightClickRowSelect(_grid, e, _headerContextMenu);
                break;
            case GridMouseClickResult.RightClickRowResult:
                GridSetupHelper.HandleRightClickRowSelect(_grid, e, _contextMenu);
                break;
        }
    }

    private void OnGridCellMouseEnter(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count)
            return;
        if (_grid.Rows[e.RowIndex].Tag is not ProcessRow processRow)
            return;

        _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ToolTipText =
            _processDisplayManager.GetProcessRowTooltip(processRow) ?? "";
    }

    private void OnGridCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
            return;

        var colName = _grid.Columns[e.ColumnIndex].Name;
        if (colName != AccountGridColumns.Logon && colName != AccountGridColumns.AllowInternet)
            return;
        if (_grid.Rows[e.RowIndex].Tag is not AccountRow accountRow || !SidResolutionHelper.IsSystemSid(accountRow.Sid))
            return;

        e.PaintBackground(e.ClipBounds, true);
        var isChecked = e.Value is true or CheckState.Checked;
        var state = isChecked ? ButtonState.Checked | ButtonState.Inactive : ButtonState.Inactive;
        var size = SystemInformation.MenuCheckSize;
        var rect = new Rectangle(
            e.CellBounds.X + (e.CellBounds.Width - size.Width) / 2,
            e.CellBounds.Y + (e.CellBounds.Height - size.Height) / 2,
            size.Width,
            size.Height);
        if (e.Graphics == null)
            return;

        ControlPaint.DrawCheckBox(e.Graphics, rect, state);
        e.Handled = true;
    }

    private void OnGridKeyPress(object? sender, KeyPressEventArgs e)
        => _typeAheadHandler.HandleKeyPress(e);

    private async void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (_grid.SelectedRows.Count == 0)
            return;

        switch (_gridInteraction.HandleKeyDown(e.KeyCode, _grid.SelectedRows[0], _processDisplayManager, IsSortActive))
        {
            case KeyAction.NoneAction:
                return;
            case KeyAction.RemoveCredentialAction:
                if (GetSelectedAccountRow() is { } removeCredentialRow)
                    _credentialHandler.RemoveCredential(removeCredentialRow, GetSelectedRowIndex());
                return;
            case KeyAction.CloseSelectedProcessAction:
                e.Handled = e.SuppressKeyPress = true;
                _contextMenuOrchestrator.TriggerCloseSelectedProcess();
                return;
            case KeyAction.BeginInlineRenameAction:
                e.Handled = e.SuppressKeyPress = true;
                _panelActions.BeginInlineRename();
                return;
            case KeyAction.EditAccountAction:
                e.Handled = e.SuppressKeyPress = true;
                if (GetSelectedAccountRow() is { } accountRow)
                    _credentialHandler.EditAccount(accountRow, GetSelectedRowIndex());
                return;
            case KeyAction.ShowContextMenuAction:
                e.Handled = true;
                GridSetupHelper.ShowContextMenuAtRow(_grid, _contextMenu);
                return;
            case KeyAction.ExpandRowAction expandRow:
                e.Handled = e.SuppressKeyPress = true;
                await ToggleProcessRowsAsync(expandRow.Sid);
                return;
            case KeyAction.CollapseRowAction collapseRow:
                e.Handled = e.SuppressKeyPress = true;
                await ToggleProcessRowsAsync(collapseRow.Sid);
                return;
            case KeyAction.NavigateToOwnerAction navigateToOwner:
                e.Handled = e.SuppressKeyPress = true;
                SelectAccountRow(navigateToOwner.OwnerSid);
                return;
        }
    }

    private async Task ToggleProcessRowsAsync(string sid)
    {
        try
        {
            await _processDisplayManager.ToggleExpandAsync(sid);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Failed to refresh process list: {ex.Message}";
        }
    }

    private void SelectAccountRow(string sid)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            var rowSid = AccountGridProcessExpander.GetSidFromRow(row);
            if (rowSid == null || !string.Equals(rowSid, sid, StringComparison.OrdinalIgnoreCase))
                continue;

            _grid.ClearSelection();
            row.Selected = true;
            _grid.CurrentCell = row.Cells[AccountGridColumns.Account];
            return;
        }
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
        => _contextMenuOrchestrator.HandleContextMenuOpening(e);

    private async void OnCreateUserClick(object? sender, EventArgs e)
        => await _creationHandler.OpenCreateUserDialog();

    private async void OpenCreateUserDialog(string? prefillUsername = null, ProtectedString? prefillPassword = null)
        => await _creationHandler.OpenCreateUserDialog(prefillUsername, prefillPassword);

    private void OnAddClick(object? sender, EventArgs e)
        => _credentialHandler.AddCredential(GetSelectedAccountRow());

    private void OnDeleteUserClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0)
            return;
        if (_grid.SelectedRows[0].Tag is not AccountRow accountRow)
            return;
        if (accountRow.IsUnavailable)
            return;

        _deletionOrchestrator.DeleteUser(accountRow, GetSelectedRowIndex());
    }

    private void OnOpenAccountsClick(object? sender, EventArgs e)
        => _panelActions.OpenUserAccountsDialog();

    private void OnCopyRandomPasswordClick(object? sender, EventArgs e)
        => _panelActions.CopyRandomPassword();

    private async void OnImportClick(object? sender, EventArgs e)
        => await _importUiHandler.HandleImportClickAsync(this);

    private async Task SaveAndRefreshAsync(Guid? selectCredentialId = null, int fallbackIndex = -1, CancellationToken cancellationToken = default)
    {
        var sidToSelect = ResolveSidForCredential(selectCredentialId);
        if (!string.IsNullOrEmpty(sidToSelect))
        {
            await _selectionSaveCoordinator.SaveRefreshAndSelectAsync(sidToSelect, cancellationToken);
            return;
        }

        _persistenceHelper.SaveCredentialStoreAndConfig(CredentialStore, Database, Session.PinDerivedKey);
        await RefreshAndNotifyDataChangedAsync(selectCredentialId, fallbackIndex, cancellationToken);
    }

    private Task RefreshAndNotifyDataChangedAsync(Guid? selectCredentialId = null, int fallbackIndex = -1, CancellationToken cancellationToken = default)
        => RefreshGridCoreAsync(() =>
        {
            if (selectCredentialId != null)
                SelectCredentialById(selectCredentialId.Value);
            else if (fallbackIndex >= 0)
                GridSetupHelper.SelectRowByIndex(_grid, fallbackIndex);
            DataChanged?.Invoke();
        }, cancellationToken);

    private string? ResolveSidForCredential(Guid? credentialId)
        => credentialId.HasValue
            ? CredentialStore.Credentials.FirstOrDefault(entry => entry.Id == credentialId.Value)?.Sid
            : null;

    private void SaveLastPrefsPath(string path)
    {
        Database.LastPrefsFilePath = path;
        _persistenceHelper.SaveConfig(Database, Session.PinDerivedKey, CredentialStore.ArgonSalt);
    }

    private void SetControlsEnabled(bool enabled)
    {
        _grid.Enabled = enabled;
        _addButton.Enabled = enabled;
        _createUserButton.Enabled = enabled;
        _createContainerButton.Enabled = enabled;
        _importButton.Enabled = enabled;
        _openCmdButton.Enabled = enabled;
        _openFolderBrowserButton.Enabled = enabled;
        _aclManagerButton.Enabled = enabled;
        _lowIntegrityAclManagerButton.Enabled = enabled;
        _appContainersAclManagerButton.Enabled = enabled;
        _copyPasswordButton.Enabled = enabled;
        _accountsButton.Enabled = enabled;
        _refreshButton.Enabled = enabled;
        _runAsButton.Enabled = enabled;
    }

    private void OnRunAsClick(object? sender, EventArgs e)
    {
        using var dlgAdapter = _openFileDialogFactory.Create();
        var dlg = dlgAdapter.Dialog;
        dlg.Title = "Run As - Select File";
        dlg.Filter = "Programs (*.exe;*.cmd;*.bat;*.com;*.lnk)|*.exe;*.cmd;*.bat;*.com;*.lnk|All Files (*.*)|*.*";
        if (dlgAdapter.ShowDialog(FindForm()) != DialogResult.OK)
            return;

        _runAsFlowHandler.TriggerFromUI(dlg.FileName, GetSelectedAccountRow()?.Sid);
    }

    private async void OnRefreshClick(object? sender, EventArgs e)
    {
        _panelActions.InvalidateLocalUserCache();
        await RefreshGridCoreAsync();
    }

    private AccountRow? GetSelectedAccountRow()
        => AccountGridHelper.GetSelectedAccountRow(_grid);

    private ContainerRow? GetSelectedContainerRow()
        => _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Tag as ContainerRow : null;

    private string? GetSelectedSid()
    {
        if (_grid.SelectedRows.Count == 0)
            return null;

        return _grid.SelectedRows[0].Tag switch
        {
            AccountRow accountRow => accountRow.Sid,
            ContainerRow containerRow when !string.IsNullOrEmpty(containerRow.ContainerSid) => containerRow.ContainerSid,
            _ => null
        };
    }

    private int GetSelectedRowIndex()
        => _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Index : -1;

    private void ShowSidMigrationWarning()
    {
        var oldImage = _migrateSidsButton.Image;
        _migrateSidsButton.Image = AccountGridHelper.CreateWarningBadgeIcon();
        _migrateSidsButton.ToolTipText = "Account SID changes detected \u2014 run SID migration";
        oldImage?.Dispose();
    }

    public bool IsOperationInProgress => _operationGuard.IsInProgress;

    private sealed class ScanProgressReporter(ToolStripButton scanButton, Label statusLabel) : IScanProgressReporter
    {
        public void SetScanEnabled(bool enabled) => scanButton.Enabled = enabled;
        public void SetStatus(string text) => statusLabel.Text = text;
    }
}
