using RunFence.Apps.UI;
using RunFence.Core.Models;
using RunFence.UI;
using RunFence.UI.Forms;

namespace RunFence.Firewall.UI.Forms;

/// <summary>
/// Dialog for editing the firewall allowlist for one account. Lets the user add IP/CIDR
/// or domain entries that bypass the internet block rule for this account.
/// </summary>
public partial class FirewallAllowlistDialog : ContextHelpForm, IFirewallAllowlistDialog, IFirewallAllowlistDialogView
{
    private readonly FirewallAllowlistEntriesController _allowlistEntriesController;
    private readonly FirewallAllowlistPortsController _portsController;
    private readonly FirewallAllowlistImportExportCoordinator _importExportCoordinator;
    private readonly FirewallBlockedConnectionsDialogController _blockedConnectionsController;
    private readonly FirewallAllowlistDialogCoordinator _dialogCoordinator;
    private readonly GridSortHelper _sortHelper = new();
    private bool _initialized;
    private List<FirewallAllowlistEntry> _result = [];
    private bool _allowInternet = true;
    private bool _allowLan = true;
    private bool _allowLocalhost = true;
    private IReadOnlyList<string> _allowedLocalhostPorts = [];
    private bool _filterEphemeralLoopback = true;

    /// <summary>
    /// Raised on each Apply click, after dialog properties are updated.
    /// Subscribers should save settings to the database and apply firewall rules immediately.
    /// Set <see cref="FirewallApplyEventArgs.RolledBack"/> to <c>true</c> when settings were
    /// rolled back after a failure, so the dialog keeps the Apply button enabled for a retry.
    /// </summary>
    public event EventHandler<FirewallApplyEventArgs>? Applied;

    /// <summary>The last applied allowlist. Only meaningful after Apply was clicked.</summary>
    public List<FirewallAllowlistEntry> Result => _result;

    /// <summary>Whether Internet access is allowed. Only meaningful after Apply was clicked.</summary>
    public bool AllowInternet => _allowInternet;

    /// <summary>Whether LAN access is allowed. Only meaningful after Apply was clicked.</summary>
    public bool AllowLan => _allowLan;

    /// <summary>Whether Localhost access is allowed. Only meaningful after Apply was clicked.</summary>
    public bool AllowLocalhost => _allowLocalhost;

    /// <summary>The last applied localhost port allow-list. Only meaningful after Apply was clicked.</summary>
    public IReadOnlyList<string> AllowedLocalhostPorts => _allowedLocalhostPorts;

    /// <summary>
    /// Whether the background scanner is enabled to block cross-user ephemeral ports.
    /// Only meaningful after Apply was clicked.
    /// </summary>
    public bool FilterEphemeralLoopback => _filterEphemeralLoopback;

    internal FirewallAllowlistDialog(
        FirewallAllowlistInitialState initialState,
        FirewallAllowlistDialogComponentFactory componentFactory)
    {
        InitializeComponent();
        InitializeRuntimeImages();
        Icon = AppIcons.GetAppIcon();
        Text = initialState.DisplayName != null ? $"Internet Allowlist \u2014 {initialState.DisplayName}" : "Internet Allowlist";

        _tooltip.SetToolTip(
            _filterEphemeralCheckBox,
            "Dynamically blocks loopback connections to services running under other accounts on ephemeral ports (49152-65535).");

        var (allowlistHandler, portsHandler) = componentFactory.CreateTabHandlers(initialState);
        var importExportHelper = componentFactory.CreateImportExportHelper(allowlistHandler, portsHandler, this);
        _allowlistEntriesController = componentFactory.CreateEntriesController(this, allowlistHandler, importExportHelper);
        _portsController = componentFactory.CreatePortsController(this, portsHandler, importExportHelper);
        _blockedConnectionsController = componentFactory.CreateBlockedConnectionsController(this, _allowlistEntriesController);
        _dialogCoordinator = componentFactory.CreateDialogCoordinator(
            initialState,
            this,
            allowlistHandler,
            portsHandler);
        _importExportCoordinator = componentFactory.CreateImportExportCoordinator(
            importExportHelper,
            _allowlistEntriesController,
            _portsController,
            this);

        _allowInternetCheckBox.Checked = initialState.AllowInternet;
        _allowLanCheckBox.Checked = initialState.AllowLan;
        _allowLocalhostCheckBox.Checked = initialState.AllowLocalhost;
        _filterEphemeralCheckBox.Checked = initialState.FilterEphemeralLoopback;

        _sortHelper.EnableThreeStateSorting(_grid, _allowlistEntriesController.PopulateGrid);
        RegisterContextHelp();
    }

    private void RegisterContextHelp()
    {
        SetContextHelp(_filterEphemeralCheckBox, ContextHelpTextCatalog.Firewall_ScopeLoopbackFilter);
        SetContextHelp(_allowlistTab, ContextHelpTextCatalog.Firewall_InternetAllowlist);
        SetContextHelp(_allowlistPanel, ContextHelpTextCatalog.Firewall_InternetAllowlist);
        SetContextHelp(_grid, ContextHelpTextCatalog.Firewall_InternetAllowlist);
        SetContextHelp(_warningLabel, ContextHelpTextCatalog.Firewall_InternetAllowlist);
        SetContextHelp(_dnsLabel, ContextHelpTextCatalog.Firewall_InternetAllowlist);
        SetContextHelp(_portsTab, ContextHelpTextCatalog.Firewall_LocalhostAllowlist);
        SetContextHelp(_portsPanel, ContextHelpTextCatalog.Firewall_LocalhostAllowlist);
        SetContextHelp(_portsGrid, ContextHelpTextCatalog.Firewall_LocalhostAllowlist);
        SetContextHelp(_portsWarningLabel, ContextHelpTextCatalog.Firewall_LocalhostAllowlist);
    }

    private void InitializeRuntimeImages()
    {
        _addButton.Image = UiIconFactory.CreateToolbarIcon("+", Color.FromArgb(0x22, 0x8B, 0x22), 30);
        _removeButton.Image = UiIconFactory.CreateToolbarIcon("\u2212", Color.FromArgb(0xCC, 0x33, 0x33), 30);
        _exportButton.Image = UiIconFactory.CreateToolbarIcon("\u2191", Color.FromArgb(0x22, 0x8B, 0x22), 30);
        _importButton.Image = UiIconFactory.CreateToolbarIcon("\u2193", Color.FromArgb(0x33, 0x66, 0xCC), 30);
        _resolveButton.Image = UiIconFactory.CreateToolbarIcon("\u21BB", Color.FromArgb(0x33, 0x66, 0x99), 30);
        _viewBlockedButton.Image = UiIconFactory.CreateToolbarIcon("\U0001F6AB", Color.FromArgb(0xCC, 0x44, 0x00), 30);
        _ctxRemoveItem.Image = UiIconFactory.CreateToolbarIcon("\u2212", Color.FromArgb(0xCC, 0x33, 0x33), 16);
        _ctxExportItem.Image = UiIconFactory.CreateToolbarIcon("\u2191", Color.FromArgb(0x22, 0x8B, 0x22), 16);
        _portsCtxRemove.Image = UiIconFactory.CreateToolbarIcon("\u2212", Color.FromArgb(0xCC, 0x33, 0x33), 16);
        _portsCtxExport.Image = UiIconFactory.CreateToolbarIcon("\u2191", Color.FromArgb(0x22, 0x8B, 0x22), 16);
    }

    private void OnFirewallSettingsChanged(object? sender, EventArgs e) => _dialogCoordinator.HandleFirewallSettingsChanged();

    private void OnTabChanged(object? sender, EventArgs e) => _dialogCoordinator.HandleSelectedTabChanged();

    private void OnGridSelectionChanged(object? sender, EventArgs e)
    {
        if (_tabControl.SelectedTab == _allowlistTab)
            _dialogCoordinator.HandleSelectedTabChanged();
    }

    private void OnPortsGridSelectionChanged(object? sender, EventArgs e)
    {
        if (_tabControl.SelectedTab == _portsTab)
            _dialogCoordinator.HandleSelectedTabChanged();
    }

    private void OnAddButtonClick(object? sender, EventArgs e)
    {
        if (_tabControl.SelectedTab == _allowlistTab)
            _allowlistEntriesController.HandleAdd();
        else
            _portsController.HandleAdd();
    }

    private void OnRemoveButtonClick(object? sender, EventArgs e)
    {
        if (_tabControl.SelectedTab == _allowlistTab)
            _allowlistEntriesController.HandleRemove();
        else
            _portsController.HandleRemove();
    }

    private void OnExportButtonClick(object? sender, EventArgs e) =>
        _importExportCoordinator.HandleExport(_tabControl.SelectedTab == _allowlistTab);

    private void OnAddClick(object? sender, EventArgs e) => _allowlistEntriesController.HandleAdd();

    private void OnRemoveClick(object? sender, EventArgs e) => _allowlistEntriesController.HandleRemove();

    private void OnExportClick(object? sender, EventArgs e) => _importExportCoordinator.HandleExport(internetTabSelected: true);

    private void OnGridCellEndEdit(object? sender, DataGridViewCellEventArgs e) =>
        _allowlistEntriesController.HandleCellEndEdit(e.RowIndex, e.ColumnIndex);

    private void OnGridMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
            _allowlistEntriesController.HandleMouseDown(e.X, e.Y);
    }

    private void OnContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e) =>
        _allowlistEntriesController.ConfigureContextMenu();

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (_allowlistEntriesController.HandleKeyDown(e.KeyCode, e.Control))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void OnPortsAddClick(object? sender, EventArgs e) => _portsController.HandleAdd();

    private void OnPortsRemoveClick(object? sender, EventArgs e) => _portsController.HandleRemove();

    private void OnPortsExportClick(object? sender, EventArgs e) => _importExportCoordinator.HandleExport(internetTabSelected: false);

    private void OnPortsGridCellEndEdit(object? sender, DataGridViewCellEventArgs e) =>
        _portsController.HandleCellEndEdit(e.RowIndex, e.ColumnIndex);

    private void OnPortsGridMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
            _portsController.HandleMouseDown(e.X, e.Y);
    }

    private void OnPortsContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e) =>
        _portsController.ConfigureContextMenu();

    private void OnPortsGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (_portsController.HandleKeyDown(e.KeyCode))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void OnImportClick(object? sender, EventArgs e) => _importExportCoordinator.HandleImport();

    /// <summary>
    /// Schedules the blocked-connections dialog to open automatically when this dialog is first shown.
    /// Passes <c>enableAuditLogging: true</c> so audit logging is activated immediately.
    /// Call this before <see cref="Form.ShowDialog()"/> - intended for the post-wizard flow.
    /// </summary>
    public void AutoOpenBlockedConnectionsOnShow()
    {
        Shown += OnAutoOpenBlockedConnections;
    }

    private void OnAutoOpenBlockedConnections(object? sender, EventArgs e)
    {
        Shown -= OnAutoOpenBlockedConnections;
        _blockedConnectionsController.OpenDialog(enableAuditLogging: true);
        UpdateApplyButton();
    }

    private void OnViewBlockedClick(object? sender, EventArgs e)
    {
        _blockedConnectionsController.OpenDialog();
        UpdateApplyButton();
    }

    private async void OnResolveClick(object? sender, EventArgs e) =>
        await _allowlistEntriesController.HandleResolveDomainsAsync(CancellationToken.None);

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_initialized)
            return;

        _initialized = true;
        _allowlistEntriesController.Initialize();
        _portsController.Initialize();
        _dialogCoordinator.Initialize();
    }

    private async void OnApplyClick(object? sender, EventArgs e) =>
        await _dialogCoordinator.ApplyAsync(CancellationToken.None);

    private void OnCloseClick(object? sender, EventArgs e) => Close();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_dialogCoordinator.ConfirmCloseWithPendingChanges())
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_dialogCoordinator.HandleShortcutKey(keyData))
            return true;

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void UpdateApplyButton() => _dialogCoordinator.UpdateApplyButtonState();

    private void RefreshToolbarState() => _dialogCoordinator.HandleSelectedTabChanged();

    private string? ShowInputPrompt(string title, string prompt)
    {
        using var dlg = new InputPromptDialog(title, prompt);
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.Value?.Trim() : null;
    }

    bool IFirewallAllowlistDialogView.IsInternetTabSelected => _tabControl.SelectedTab == _allowlistTab;
    bool IFirewallAllowlistDialogView.IsResolvingDomains => _allowlistEntriesController.IsResolvingDomains;
    int IFirewallAllowlistDialogView.SelectedAllowlistRowCount => _grid.SelectedRows.Count;
    int IFirewallAllowlistDialogView.SelectedPortRowCount => _portsGrid.SelectedRows.Count;
    bool IFirewallAllowlistDialogView.AllowInternetChecked => _allowInternetCheckBox.Checked;
    bool IFirewallAllowlistDialogView.AllowLanChecked => _allowLanCheckBox.Checked;
    bool IFirewallAllowlistDialogView.AllowLocalhostChecked => _allowLocalhostCheckBox.Checked;
    bool IFirewallAllowlistDialogView.FilterEphemeralChecked => _filterEphemeralCheckBox.Checked;
    DataGridView IFirewallAllowlistDialogView.AllowlistGrid => _grid;
    DataGridView IFirewallAllowlistDialogView.PortsGrid => _portsGrid;

    string? IFirewallAllowlistDialogView.PromptInput(string title, string prompt) =>
        ShowInputPrompt(title, prompt);

    void IFirewallAllowlistDialogView.SetDnsLabelText(string text) => _dnsLabel.Text = text;

    void IFirewallAllowlistDialogView.SetFilterEphemeralEnabled(bool enabled) =>
        _filterEphemeralCheckBox.Enabled = enabled;

    void IFirewallAllowlistDialogView.SetWarningVisibility(bool internetWarningVisible, bool portsWarningVisible)
    {
        _warningLabel.Visible = internetWarningVisible;
        _portsWarningLabel.Visible = portsWarningVisible;
    }

    void IFirewallAllowlistDialogView.SetToolbarState(
        bool addEnabled,
        string addToolTipText,
        bool removeEnabled,
        string removeToolTipText,
        string exportToolTipText,
        bool resolveEnabled,
        bool viewBlockedEnabled)
    {
        _addButton.Enabled = addEnabled;
        _addButton.ToolTipText = addToolTipText;
        _removeButton.Enabled = removeEnabled;
        _removeButton.ToolTipText = removeToolTipText;
        _exportButton.ToolTipText = exportToolTipText;
        _resolveButton.Enabled = resolveEnabled;
        _viewBlockedButton.Enabled = viewBlockedEnabled;
    }

    void IFirewallAllowlistDialogView.SetInteractionEnabled(bool enabled, bool filterEphemeralEnabled)
    {
        _closeButton.Enabled = enabled;
        _tabControl.Enabled = enabled;
        _toolStrip.Enabled = enabled;
        _allowInternetCheckBox.Enabled = enabled;
        _allowLanCheckBox.Enabled = enabled;
        _allowLocalhostCheckBox.Enabled = enabled;
        _filterEphemeralCheckBox.Enabled = enabled && filterEphemeralEnabled;
    }

    void IFirewallAllowlistDialogView.SetApplyButtonEnabled(bool enabled) => _applyButton.Enabled = enabled;

    void IFirewallAllowlistDialogView.CommitGridEdits()
    {
        _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        _portsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    void IFirewallAllowlistDialogView.RaiseApplied(FirewallApplyEventArgs args) =>
        Applied?.Invoke(this, args);

    void IFirewallAllowlistDialogView.SetAppliedValues(
        List<FirewallAllowlistEntry> result,
        bool allowInternet,
        bool allowLan,
        bool allowLocalhost,
        IReadOnlyList<string> allowedLocalhostPorts,
        bool filterEphemeralLoopback)
    {
        _result = result;
        _allowInternet = allowInternet;
        _allowLan = allowLan;
        _allowLocalhost = allowLocalhost;
        _allowedLocalhostPorts = allowedLocalhostPorts;
        _filterEphemeralLoopback = filterEphemeralLoopback;
    }

    DialogResult IFirewallAllowlistDialogView.ShowDiscardChangesPrompt() =>
        MessageBox.Show(
            "You have unapplied changes. Discard and close?",
            "Internet Allowlist",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

    void IFirewallAllowlistDialogView.ShowInformation(string title, string message) =>
        MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    void IFirewallAllowlistDialogView.ShowWarning(string title, string message) =>
        MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    void IFirewallAllowlistDialogView.ShowError(string title, string message) =>
        MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);

    void IFirewallAllowlistDialogView.RequestClose() => Close();

    void IFirewallAllowlistDialogView.UpdateApplyButton() => UpdateApplyButton();

    void IFirewallAllowlistDialogView.RefreshToolbarState() => RefreshToolbarState();
}
