using System.ComponentModel;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.UI;
using RunFence.UI.Forms;
using RunFence.Wizard.UI;

namespace RunFence.Account.UI.Forms;

public partial class AccountsPanel : DataPanel,
    IAccountsPanelContext,
    IGridSortState, IAccountGridCallbacks
{
    private readonly IAccountCredentialManager _credentialManager;
    private readonly ILoggingService _log;
    private readonly OperationGuard _operationGuard;
    private bool _staleDone;
    private Form? _parentForm;

    private readonly AccountContainerOrchestrator _containerHandler;
    private readonly AccountBulkScanHandler? _bulkScanHandler;

    // High-level extracted services
    private readonly AccountsPanelLaunchService _launchService;
    private readonly AccountsPanelTimerCoordinator _timerCoordinator;
    private readonly AccountSidMigrationLauncher _migrationLauncher;
    private readonly AccountProcessDisplayManager _processDisplayManager;
    private readonly AccountsPanelGridInteraction _gridInteraction;

    // Injected handlers (initialized in BuildDynamicContent)
    private readonly AccountsPanelRefreshOrchestrator _refreshOrchestrator;
    private readonly AccountContextMenuOrchestrator _contextMenuOrchestrator;
    private readonly AccountsPanelCredentialHandler _credentialHandler;
    private readonly AccountCreationOrchestrator _creationHandler;
    private readonly AccountPanelActions _panelActions;
    private readonly AccountImportUIHandler _importUIHandler;
    private readonly AccountGridEditHandler _gridEditHandler;
    private readonly AccountGridTypeAheadHandler _typeAheadHandler;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool RenameInProgress
    {
        get => _gridEditHandler.RenameInProgress;
        set => _gridEditHandler.RenameInProgress = value;
    }

    public event Action? DataChanged;
    public event Action<string>? AppNavigationRequested;
    public event Action<string>? NewAppRequested;

    // IAccountsPanelContext implementation
    AppDatabase IAccountsPanelContext.Database => Database;
    CredentialStore IAccountsPanelContext.CredentialStore => CredentialStore;
    Control IAccountsPanelContext.OwnerControl => this;
    OperationGuard IAccountsPanelContext.OperationGuard => _operationGuard;
    bool IAccountsPanelContext.IsRefreshing => IsRefreshing;
    DialogResult IAccountsPanelContext.ShowModal(Form dialog) => ShowModalDialog(dialog);
    void IAccountsPanelContext.SaveAndRefresh(Guid? selectCredentialId, int fallbackIndex) => SaveAndRefresh(selectCredentialId, fallbackIndex);
    void IAccountsPanelContext.UpdateStatus(string text) => _statusLabel.Text = text;
    void IAccountsPanelContext.UpdateButtonState() => UpdateButtonState();
    void IAccountsPanelContext.RefreshGrid() => RefreshGrid();

    void IAccountsPanelContext.TriggerProcessRefresh(int delayMs)
        => _processDisplayManager.TriggerDelayedRefresh(delayMs);

    // IGridSortState implementation
    bool IGridSortState.IsSortActive => IsSortActive;
    int IGridSortState.SortColumnIndex => SortColumnIndex;
    bool IGridSortState.SortDescending => SortDirection == SortOrder.Descending;

    // IAccountGridCallbacks implementation
    void IAccountGridCallbacks.ReapplyGlyph() => ReapplyGlyphIfActive(_grid);
    void IAccountGridCallbacks.SelectFirstRow() => SelectFirstRow(_grid);
    void IAccountGridCallbacks.UpdateButtonState() => UpdateButtonState();
    void IAccountGridCallbacks.SetIsRefreshing() => IsRefreshing = true;
    void IAccountGridCallbacks.ClearIsRefreshing() => IsRefreshing = false;
    void IAccountGridCallbacks.UpdateStatus(string text) => _statusLabel.Text = text;
    void IAccountGridCallbacks.ClearStatus() => _statusLabel.Text = "";

    public AccountsPanel(
        IAccountCredentialManager credentialManager,
        ILoggingService log,
        AccountContainerOrchestrator containerHandler,
        AccountsPanelRefreshOrchestrator refreshOrchestrator,
        AccountContextMenuOrchestrator contextMenuOrchestrator,
        AccountsPanelCredentialHandler credentialHandler,
        AccountCreationOrchestrator creationHandler,
        AccountPanelActions panelActions,
        OperationGuard operationGuard,
        AccountsPanelLaunchService launchService,
        AccountsPanelTimerCoordinator timerCoordinator,
        AccountSidMigrationLauncher migrationLauncher,
        AccountsPanelGridInteraction gridInteraction,
        AccountProcessDisplayManager processDisplayManager,
        AccountImportUIHandler importUIHandler,
        AccountGridEditHandler gridEditHandler,
        AccountGridTypeAheadHandler typeAheadHandler,
        AccountBulkScanHandler? bulkScanHandler = null,
        WizardLauncher? wizardButtonHandler = null)
    {
        _credentialManager = credentialManager;
        _log = log;
        _containerHandler = containerHandler;
        _refreshOrchestrator = refreshOrchestrator;
        _contextMenuOrchestrator = contextMenuOrchestrator;
        _credentialHandler = credentialHandler;
        _creationHandler = creationHandler;
        _panelActions = panelActions;
        _operationGuard = operationGuard;
        _launchService = launchService;
        _timerCoordinator = timerCoordinator;
        _migrationLauncher = migrationLauncher;
        _gridInteraction = gridInteraction;
        _processDisplayManager = processDisplayManager;
        _importUIHandler = importUIHandler;
        _gridEditHandler = gridEditHandler;
        _typeAheadHandler = typeAheadHandler;
        _bulkScanHandler = bulkScanHandler;

        InitializeComponent();

        _grid.CellMouseEnter += OnGridCellMouseEnter;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.Empty;
        EnableThreeStateSorting(_grid, () => { RefreshGrid(); }, sectioned: true);

        BuildDynamicContent(wizardButtonHandler);
    }

    private void BuildDynamicContent(WizardLauncher? wizardButtonHandler)
    {
        // Toolbar images (runtime bitmap generation)
        _refreshButton.Image = UiIconFactory.CreateToolbarIcon("\u21BB", Color.FromArgb(0x33, 0x66, 0x99), 48);
        _addButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F511", Color.FromArgb(0x22, 0x8B, 0x22), 42);
        _createUserButton.Image = AccountGridHelper.CreateUserProfileIcon(42);
        _createContainerButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F4E6", Color.FromArgb(0x33, 0x66, 0xCC), 42);
        _createContainerButton.Visible = true;
        _openCmdButton.Image = UiIconFactory.CreateToolbarIcon(">", Color.FromArgb(0x33, 0x33, 0x33), 42);
        _openCmdButton.ToolTipText = "Open CMD (hold Shift to launch with full privileges)";
        _openFolderBrowserButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F4C2", Color.FromArgb(0xCC, 0x88, 0x22), 42);
        _openFolderBrowserButton.ToolTipText = "Open Folder Browser (hold Shift to launch with full privileges)";
        _copyPasswordButton.Image = UiIconFactory.CreateToolbarIcon("\u26BF", Color.FromArgb(0x66, 0x66, 0x99), 42);
        _importButton.Image = UiIconFactory.CreateToolbarIcon("\u2193", Color.FromArgb(0x33, 0x66, 0x99), 42);
        _accountsButton.Image = UiIconFactory.CreateToolbarIcon("\u2699", Color.FromArgb(0x33, 0x66, 0x99), 42);
        _migrateSidsButton.Image = UiIconFactory.CreateToolbarIcon("\u21C4", Color.FromArgb(0x66, 0x66, 0x99), 42);
        _deleteProfilesButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F5D1", Color.FromArgb(0x99, 0x33, 0x33), 42);
        _aclManagerButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F4DC", Color.FromArgb(0xCC, 0x99, 0x00), 42);
        _scanAclsButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F50D", Color.FromArgb(0x22, 0x88, 0x44), 42);
        _scanAclsButton.Visible = _bulkScanHandler != null;
        _firewallButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F310", Color.FromArgb(0x22, 0x66, 0xCC), 42);
        _firewallButton.ToolTipText = "Internet Whitelist";
        _firewallButton.Visible = _contextMenuOrchestrator.IsFirewallAvailable;
        _wizardButton.Image = UiIconFactory.CreateToolbarIcon("\u2728", Color.FromArgb(0x33, 0x66, 0x99), 42);
        _wizardButton.Visible = wizardButtonHandler != null;
        if (wizardButtonHandler != null)
        {
            _wizardButton.Click += (_, _) => wizardButtonHandler.OpenWizard(this);
            wizardButtonHandler.WizardCompleted += () => DataChanged?.Invoke();
        }

        _hdrAdd.Image = UiIconFactory.CreateToolbarIcon("\U0001F511", Color.FromArgb(0x22, 0x8B, 0x22), 16);
        _hdrCreateUser.Image = AccountGridHelper.CreateUserProfileIcon(16);
        _hdrCreateContainer.Image = UiIconFactory.CreateToolbarIcon("\U0001F4E6", Color.FromArgb(0x33, 0x66, 0xCC), 16);
        _hdrCreateContainer.Visible = true;

        // Initialize handlers with grid + callbacks
        _processDisplayManager.Initialize(_grid, this);
        _refreshOrchestrator.Initialize(_grid, this, this, _processDisplayManager);
        _contextMenuOrchestrator.Initialize(_grid, _contextMenu, this, _hdrCreateContainer, _processDisplayManager);
        _credentialHandler.Initialize(this, this);
        _panelActions.Initialize(_grid, this);
        _importUIHandler.Initialize(_grid, _importButton);
        _gridEditHandler.Initialize(_grid, this);
        _typeAheadHandler.Initialize(_grid);

        // Wire context menu orchestrator events to panel events
        _contextMenuOrchestrator.AppNavigationRequested += sid => AppNavigationRequested?.Invoke(sid);
        _contextMenuOrchestrator.NewAppRequested += sid => NewAppRequested?.Invoke(sid);
        _contextMenuOrchestrator.DataChangedAndRefresh += () =>
        {
            DataChanged?.Invoke();
            RefreshGrid();
        };
        _contextMenuOrchestrator.InstallRequested += package =>
        {
            if (GetSelectedAccountRow() is { } ar)
            {
                if (package == KnownPackages.WindowsTerminal && !_launchService.IsPackageInstalled(KnownPackages.Winget, ar.Sid))
                    _launchService.InstallPackages([KnownPackages.Winget, package], ar);
                else
                    _launchService.InstallPackage(package, ar);
            }
        };
        _contextMenuOrchestrator.OpenFolderBrowserRequested += OnOpenFolderBrowserClick;
        _contextMenuOrchestrator.OpenCmdRequested += OnOpenCmdClick;
        _contextMenuOrchestrator.EnvironmentVariablesRequested += OnEnvironmentVariablesClick;

        _aclManagerButton.Click += (_, _) => _contextMenuOrchestrator.OpenAclManager();
        _firewallButton.Click += (_, _) => _contextMenuOrchestrator.OpenFirewallAllowlist();

        // Wire credential handler events
        _credentialHandler.CreateUserDialogRequested += (username, password) => OpenCreateUserDialog(username, password);
        _credentialHandler.DeleteUserRequested += (accountRow, selectedIndex) => _creationHandler.DeleteUser(accountRow, selectedIndex);
        _credentialHandler.InstallPackagesRequested += (packages, ar) => _launchService.InstallPackages(packages, ar);
        _credentialHandler.SaveAndRefreshRequested += (credId, fallbackIndex) => SaveAndRefresh(credId, fallbackIndex);

        // Wire creation handler events
        _creationHandler.InstallPackagesRequested += (packages, cred, pwd) => _launchService.InstallPackages(packages, cred, pwd);
        _creationHandler.SaveAndRefreshRequested += (credId, fallbackIndex) => SaveAndRefresh(credId, fallbackIndex);
        _creationHandler.StatusUpdateRequested += text => _statusLabel.Text = text;
        _creationHandler.OperationStarted += () => _operationGuard.Begin(this);
        _creationHandler.OperationEnded += () => _operationGuard.End(this);

        // Wire context menu items click handlers for Edit/Password operations
        var items = _contextMenuOrchestrator.Items;
        items.FirewallAllowlist.Image = UiIconFactory.CreateToolbarIcon("\U0001F310", Color.FromArgb(0x22, 0x66, 0xCC), 16);
        items.EditAccount.Click += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } ar)
                _credentialHandler.EditAccount(ar, GetSelectedRowIndex());
        };
        items.EditCredential.Click += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } ar)
                _credentialHandler.EditCredential(ar);
        };
        items.RemoveCredential.Click += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } ar)
                _credentialHandler.RemoveCredential(ar, GetSelectedRowIndex());
        };
        items.DeleteUser.Click += OnDeleteUserClick;
        items.PinFolderBrowserToTray.Click += OnPinFolderBrowserToTrayClick;
        items.PinDiscoveryToTray.Click += OnPinDiscoveryToTrayClick;
        items.PinTerminalToTray.Click += OnPinTerminalToTrayClick;
        items.CopySid.Click += (_, _) =>
        {
            var sid = GetSelectedSid();
            if (sid != null)
                _panelActions.CopySid(sid);
        };
        items.CopyProfilePath.Click += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } ar)
                _panelActions.CopyProfilePath(ar.Sid);
        };
        items.OpenProfileFolder.Click += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } ar)
                _panelActions.OpenProfileFolder(ar.Sid);
        };
        items.CopyPassword.Click += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } ar)
                _credentialHandler.CopyPassword(ar);
        };
        items.TypePassword.Click += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } ar)
                _credentialHandler.TypePassword(ar);
        };
        items.RotatePassword.Click += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } ar)
                _credentialHandler.RotatePassword(ar);
        };
        items.SetEmptyPassword.Click += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } ar)
                _credentialHandler.SetEmptyPassword(ar);
        };
        _contextMenuOrchestrator.EditCredentialRequested += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } ar)
                _credentialHandler.EditCredential(ar);
        };

        _grid.KeyPress += OnGridKeyPress;
        _grid.CellBeginEdit += (_, e) => _gridEditHandler.HandleCellBeginEdit(e);
        _grid.CellEndEdit += (_, e) => _gridEditHandler.HandleCellEndEdit(e);
        _grid.CurrentCellDirtyStateChanged += (_, _) => _gridEditHandler.HandleDirtyStateChanged();
        _grid.CellValueChanged += (_, e) => _gridEditHandler.HandleCellValueChanged(e);
        _grid.CellValidating += (_, e) => _gridEditHandler.HandleCellValidating(e);
        _grid.EditingControlShowing += (_, e) => _gridEditHandler.HandleEditingControlShowing(e);

        // Wire timer coordinator events
        _timerCoordinator.SidChangeDetected += OnSidChangeDetected;
        _timerCoordinator.RefreshNeeded += () =>
        {
            if (!IsRefreshing && !_operationGuard.IsInProgress)
                RefreshGrid();
        };
    }

    // Forwarding handlers for Designer-wired events and internal call sites
    private void OnCreateContainerClick(object? sender, EventArgs e)
    {
        var session = Session;
        _containerHandler.CreateContainer(session.Database, session.CredentialStore, session.PinDerivedKey,
            FindForm(), () =>
            {
                DataChanged?.Invoke();
                RefreshGrid();
            });
    }

    private void OnAclManagerClick(object? sender, EventArgs e)
        => _contextMenuOrchestrator.OpenAclManager();

    private async void OnScanAclsClick(object? sender, EventArgs e)
    {
        if (_bulkScanHandler == null)
            return;
        await _bulkScanHandler.ScanAcls(this, _scanAclsButton, _statusLabel);
    }

    protected override void OnDataSet()
    {
        if (!_staleDone)
        {
            _staleDone = true;
            _ = InitialRefreshAsync();
            _timerCoordinator.Start();
            _processDisplayManager.Start(() => Visible && IsParentFormVisible());
        }
        else
        {
            RefreshGrid();
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        SubscribeToParentFormResize();
    }

    private void SubscribeToParentFormResize()
    {
        if (FindForm() is { } form)
        {
            _parentForm = form;
            form.Resize += OnParentFormResize;
        }
    }

    private void OnParentFormResize(object? sender, EventArgs e)
    {
        bool isMinimized = FindForm()?.WindowState == FormWindowState.Minimized;
        _processDisplayManager.NotifyParentResized(isMinimized);
    }

    private void OnSidChangeDetected()
    {
        var oldImage = _migrateSidsButton.Image;
        _migrateSidsButton.Image = AccountGridHelper.CreateWarningBadgeIcon();
        _migrateSidsButton.ToolTipText = "Account SID changes detected \u2014 run SID migration";
        oldImage?.Dispose();
    }

    public override void RefreshOnActivation()
    {
        if (!_staleDone || IsRefreshing || _operationGuard.IsInProgress || IsSortActive)
            return;
        RefreshGrid();
        if (!IsSortActive)
            _processDisplayManager.TriggerImmediateRefresh();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        bool isVisible = Visible && IsParentFormVisible();
        _timerCoordinator.NotifyVisibilityChanged(isVisible);
        _processDisplayManager.NotifyVisibilityChanged(isVisible);
        if (isVisible)
            RefreshOnActivation();
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
            _migrationLauncher.LaunchMigrationDialog(Session, FindForm(), () =>
            {
                DataChanged?.Invoke();
                RefreshGrid();
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

    private async Task InitialRefreshAsync()
        => await _refreshOrchestrator.InitialRefreshAsync();

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

    // --- Grid population ---

    private void RefreshGrid(Action? afterPopulate = null)
        => _refreshOrchestrator.RefreshGrid(afterPopulate);

    protected override void UpdateButtonState()
    {
        var hasSelection = _grid.SelectedRows.Count > 0;
        IAccountGridRow? gridRow = hasSelection ? _grid.SelectedRows[0].Tag as IAccountGridRow : null;
        AccountRow? accountRow = gridRow as AccountRow;
        var containerRow = gridRow as ContainerRow;

        var hasAccountRow = accountRow != null;
        var isUnavailable = accountRow?.IsUnavailable == true;
        var canLaunch = hasAccountRow && !isUnavailable &&
                        (SidResolutionHelper.CanLaunchWithoutPassword(accountRow?.Sid) || accountRow?.HasStoredPassword == true);

        _openCmdButton.Enabled = canLaunch || containerRow != null;
        _openFolderBrowserButton.Enabled = canLaunch || containerRow != null;
        _aclManagerButton.Enabled =
            (accountRow != null && !isUnavailable) ||
            (containerRow != null && !string.IsNullOrEmpty(containerRow.ContainerSid));
        _firewallButton.Enabled = hasAccountRow && !isUnavailable && !string.IsNullOrEmpty(accountRow?.Sid);

        var useWindowsTerminal = accountRow != null && _launchService.IsWindowsTerminal(accountRow.Sid);
        _openCmdButton.ToolTipText = useWindowsTerminal
            ? "Open Terminal (hold Shift to launch with full privileges)"
            : "Open CMD (hold Shift to launch with full privileges)";

        if (_grid.Columns["Import"]?.Visible == true)
            _importButton.Enabled = true;
    }

    // --- Grid event handlers ---

    private void OnGridCellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
            return;
        var row = _grid.Rows[e.RowIndex];
        var colName = _grid.Columns[e.ColumnIndex].Name;

        var action = _gridInteraction.HandleCellContentClick(row, colName);
        switch (action)
        {
            case GridClickAction.ToggleLogonAction t:
                _gridEditHandler.HandleLogonToggle(row, t.AccountRow);
                break;
            case GridClickAction.ToggleInternetAction t:
                _gridEditHandler.HandleAllowInternetToggle(row, t.AccountRow);
                break;
            case GridClickAction.ToggleContainerInternetAction t:
                HandleContainerInternetToggle(row, t.ContainerRow);
                break;
        }
    }

    private void HandleContainerInternetToggle(DataGridViewRow row, ContainerRow containerRow)
    {
        _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        var cell = (DataGridViewCheckBoxCell)row.Cells["colAllowInternet"];
        var enable = cell.Value is true or CheckState and CheckState.Checked;

        var session = Session;
        _containerHandler.ToggleContainerInternet(containerRow, enable, session.Database, session.CredentialStore, session.PinDerivedKey,
            () =>
            {
                DataChanged?.Invoke();
                RefreshGrid();
            });
    }

    private void SelectByTag<T>(Func<T, bool> predicate) where T : class, IAccountGridRow
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is T t && predicate(t))
            {
                row.Selected = true;
                _grid.CurrentCell = row.Cells["Account"];
                return;
            }
        }
    }

    public void SelectBySid(string sid)
        => SelectByTag<AccountRow>(ar => string.Equals(ar.Sid, sid, StringComparison.OrdinalIgnoreCase));

    private void SelectCredentialById(Guid credId)
        => SelectByTag<AccountRow>(ar => ar.Credential?.Id == credId);

    private void OnGridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
            return;
        var action = _gridInteraction.HandleDoubleClick(_grid.Rows[e.RowIndex]);
        if (action == GridDoubleClickAction.OpenAclManager)
            OnAclManagerClick(sender, e);
    }

    private void OnGridCellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        var result = _gridInteraction.HandleMouseClick(e, _grid, _processDisplayManager, IsSortActive);
        switch (result)
        {
            case GridMouseClickResult.ExpandToggleResult r:
                _processDisplayManager.ToggleExpand(r.Sid);
                break;
            case GridMouseClickResult.RightClickHeaderResult:
                HandleRightClickRowSelect(_grid, e, _headerContextMenu);
                break;
            case GridMouseClickResult.RightClickRowResult:
                HandleRightClickRowSelect(_grid, e, _contextMenu);
                break;
        }
    }

    private void OnGridCellMouseEnter(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count)
            return;
        if (_grid.Rows[e.RowIndex].Tag is not ProcessRow processRow)
            return;
        var tooltip = _processDisplayManager.GetProcessRowTooltip(processRow) ?? "";
        _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ToolTipText = tooltip;
    }

    private void OnGridKeyPress(object? sender, KeyPressEventArgs e)
        => _typeAheadHandler.HandleKeyPress(e);

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (_grid.SelectedRows.Count == 0)
            return;
        var action = _gridInteraction.HandleKeyDown(e.KeyCode, _grid.SelectedRows[0], _processDisplayManager, IsSortActive);
        switch (action)
        {
            case KeyAction.NoneAction:
                return;
            case KeyAction.RemoveCredentialAction:
            {
                if (GetSelectedAccountRow() is { } ar)
                    _credentialHandler.RemoveCredential(ar, GetSelectedRowIndex());
                return;
            }
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
            {
                if (GetSelectedAccountRow() is { } ar)
                    _credentialHandler.EditAccount(ar, GetSelectedRowIndex());
                return;
            }
            case KeyAction.ShowContextMenuAction:
                e.Handled = true;
                ShowContextMenuAtSelectedRow();
                return;
            case KeyAction.ExpandRowAction r:
                e.Handled = e.SuppressKeyPress = true;
                _processDisplayManager.ToggleExpand(r.Sid);
                return;
            case KeyAction.CollapseRowAction r:
                e.Handled = e.SuppressKeyPress = true;
                _processDisplayManager.ToggleExpand(r.Sid);
                return;
            case KeyAction.NavigateToOwnerAction r:
                e.Handled = e.SuppressKeyPress = true;
                SelectAccountRow(r.OwnerSid);
                return;
        }
    }

    private void SelectAccountRow(string sid)
    {
        foreach (DataGridViewRow gridRow in _grid.Rows)
        {
            string? rowSid = AccountGridProcessExpander.GetSidFromRow(gridRow);
            if (rowSid != null && string.Equals(rowSid, sid, StringComparison.OrdinalIgnoreCase))
            {
                _grid.ClearSelection();
                gridRow.Selected = true;
                _grid.CurrentCell = gridRow.Cells["Account"];
                return;
            }
        }
    }

    private void ShowContextMenuAtSelectedRow()
    {
        ShowContextMenuAtRow(_grid, _contextMenu);
    }

    // --- Context menu ---

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
        => _contextMenuOrchestrator.HandleContextMenuOpening(e);

    // --- User and credential operations ---

    private void OnCreateUserClick(object? sender, EventArgs e)
        => _creationHandler.OpenCreateUserDialog();

    private void OpenCreateUserDialog(string? prefillUsername = null, string? prefillPassword = null)
        => _creationHandler.OpenCreateUserDialog(prefillUsername, prefillPassword);

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
        var selectedIndex = _grid.SelectedRows[0].Index;
        _creationHandler.DeleteUser(accountRow, selectedIndex);
    }

    // --- Launch operations ---

    private void OnOpenCmdClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0)
            return;
        if (_grid.SelectedRows[0].Tag is ContainerRow containerRow)
        {
            var session = Session;
            _containerHandler.LaunchCmd(containerRow, session.Database, session.CredentialStore, session.PinDerivedKey);
            return;
        }

        if (_grid.SelectedRows[0].Tag is not AccountRow accountRow)
            return;
        var flags = (ModifierKeys & Keys.Shift) != 0
            ? default
            : LaunchFlags.FromAccountDefaults(Database, accountRow.Sid);
        _launchService.OpenCmd(accountRow, flags);
    }

    private void OnEnvironmentVariablesClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not AccountRow accountRow)
            return;
        var flags = (ModifierKeys & Keys.Shift) != 0
            ? default
            : LaunchFlags.FromAccountDefaults(Database, accountRow.Sid);
        _launchService.OpenEnvironmentVariables(accountRow, flags);
    }

    private void OnOpenFolderBrowserClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0)
            return;
        if (_grid.SelectedRows[0].Tag is ContainerRow containerRow)
        {
            var session = Session;
            _containerHandler.LaunchFolderBrowser(containerRow, session.Database.Settings, session.Database, session.CredentialStore, session.PinDerivedKey);
            return;
        }

        if (_grid.SelectedRows[0].Tag is not AccountRow accountRow)
            return;
        var flags = (ModifierKeys & Keys.Shift) != 0
            ? default
            : LaunchFlags.FromAccountDefaults(Database, accountRow.Sid);
        _launchService.OpenFolderBrowser(accountRow, flags, FindForm());
    }

    private void OnPinFolderBrowserToTrayClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not AccountRow accountRow)
            return;
        if (string.IsNullOrEmpty(accountRow.Sid))
            return;
        _launchService.ToggleFolderBrowserTray(accountRow, () => DataChanged?.Invoke());
    }

    private void OnPinDiscoveryToTrayClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not AccountRow accountRow)
            return;
        if (string.IsNullOrEmpty(accountRow.Sid))
            return;
        _launchService.ToggleDiscoveryTray(accountRow, () => DataChanged?.Invoke());
    }

    private void OnPinTerminalToTrayClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not AccountRow accountRow)
            return;
        if (string.IsNullOrEmpty(accountRow.Sid))
            return;
        _launchService.ToggleTerminalTray(accountRow, () => DataChanged?.Invoke());
    }

    // --- Account operations ---

    private void OnOpenAccountsClick(object? sender, EventArgs e)
    {
        try
        {
            _panelActions.OpenUserAccountsDialog();
        }
        catch (Exception ex)
        {
            _log.Error("Failed to open User Accounts dialog", ex);
            MessageBox.Show($"Failed to open dialog: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnCopyRandomPasswordClick(object? sender, EventArgs e)
        => _panelActions.CopyRandomPassword();

    // --- Import ---

    private async void OnImportClick(object? sender, EventArgs e)
        => await _importUIHandler.HandleImportClickAsync(
            Database, CredentialStore, PinDerivedKey, FindForm(), this,
            _operationGuard,
            text => _statusLabel.Text = text,
            SetControlsEnabled,
            UpdateButtonState,
            SaveLastPrefsPath);

    // --- Helpers ---

    private void SaveAndRefresh(Guid? selectCredId = null, int fallbackIndex = -1)
    {
        _credentialManager.SaveCredentialStoreAndConfig(CredentialStore, Database, PinDerivedKey);
        RefreshGrid(afterPopulate: () =>
        {
            if (selectCredId != null)
                SelectCredentialById(selectCredId.Value);
            else if (fallbackIndex >= 0)
                SelectRowByIndex(_grid, fallbackIndex);
            DataChanged?.Invoke();
        });
    }

    private void SaveLastPrefsPath(string path)
    {
        Database.LastPrefsFilePath = path;
        _credentialManager.SaveConfig(Database, PinDerivedKey, CredentialStore.ArgonSalt);
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
        _copyPasswordButton.Enabled = enabled;
        _accountsButton.Enabled = enabled;
        _refreshButton.Enabled = enabled;
    }

    private void OnRefreshClick(object? sender, EventArgs e)
    {
        _panelActions.InvalidateLocalUserCache();
        RefreshGrid();
    }

    // --- Grid selection helpers ---

    private AccountRow? GetSelectedAccountRow()
        => _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Tag as AccountRow : null;

    private string? GetSelectedSid()
    {
        if (_grid.SelectedRows.Count == 0)
            return null;
        return _grid.SelectedRows[0].Tag switch
        {
            AccountRow ar => ar.Sid,
            ContainerRow cr when !string.IsNullOrEmpty(cr.ContainerSid) => cr.ContainerSid,
            _ => null
        };
    }

    private int GetSelectedRowIndex()
        => _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Index : -1;

    public bool IsOperationInProgress => _operationGuard.IsInProgress;
}