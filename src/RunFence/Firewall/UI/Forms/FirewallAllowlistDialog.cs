using RunFence.Apps.UI;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.UI;
using RunFence.UI.Forms;

namespace RunFence.Firewall.UI.Forms;

/// <summary>
/// Dialog for editing the firewall allowlist for one account. Lets the user add IP/CIDR
/// or domain entries that bypass the internet block rule for this account.
/// </summary>
public partial class FirewallAllowlistDialog : RunFence.UI.Forms.ContextHelpForm, IFirewallAllowlistDialog
{
    private readonly IFirewallNetworkInfo _firewallNetworkInfo;
    private readonly FirewallAllowlistTabHandler _allowlistHandler;
    private readonly FirewallPortsTabHandler _portsHandler;
    private readonly FirewallAllowlistGridHelper _allowlistGridHelper;
    private readonly FirewallPortsGridHelper _portsGridHelper;
    private readonly BlockedConnectionsFlowHelper _blockedConnectionsFlow;
    private readonly FirewallDialogApplyPresenter _applyPresenter;
    private FirewallAllowlistImportExportHelper _importExportHelper = null!;
    private bool _initialAllowInternet;
    private bool _initialAllowLan;
    private bool _initialAllowLocalhost;
    private bool _initialFilterEphemeral;
    private bool _isApplying;
    private readonly GridSortHelper _sortHelper = new();

    /// <summary>
    /// Raised on each Apply click, after dialog properties are updated.
    /// Subscribers should save settings to the database and apply firewall rules immediately.
    /// Set <see cref="FirewallApplyEventArgs.RolledBack"/> to <c>true</c> when settings were
    /// rolled back after a failure, so the dialog keeps the Apply button enabled for a retry.
    /// </summary>
    public event EventHandler<FirewallApplyEventArgs>? Applied;

    /// <summary>The last applied allowlist. Only meaningful after Apply was clicked.</summary>
    public List<FirewallAllowlistEntry> Result { get; private set; } = [];

    /// <summary>Whether Internet access is allowed. Only meaningful after Apply was clicked.</summary>
    public bool AllowInternet { get; private set; } = true;

    /// <summary>Whether LAN access is allowed. Only meaningful after Apply was clicked.</summary>
    public bool AllowLan { get; private set; } = true;

    /// <summary>Whether Localhost access is allowed. Only meaningful after Apply was clicked.</summary>
    public bool AllowLocalhost { get; private set; } = true;

    /// <summary>The last applied localhost port allow-list. Only meaningful after Apply was clicked.</summary>
    public IReadOnlyList<string> AllowedLocalhostPorts { get; private set; } = [];

    /// <summary>
    /// Whether the background scanner is enabled to block cross-user ephemeral ports.
    /// Only meaningful after Apply was clicked.
    /// </summary>
    public bool FilterEphemeralLoopback { get; private set; } = true;

    internal FirewallAllowlistDialog(
        List<FirewallAllowlistEntry> current,
        IFirewallNetworkInfo firewallNetworkInfo,
        FirewallAllowlistValidator validator,
        FirewallPortValidator portValidator,
        FirewallDomainResolver domainResolver,
        BlockedConnectionsFlowHelper blockedConnectionsFlow,
        FirewallAllowlistImportExportService importExportService,
        FirewallDialogApplyPresenter applyPresenter,
        string? displayName = null,
        bool allowInternet = true,
        bool allowLan = true,
        bool allowLocalhost = true,
        IReadOnlyList<string>? allowedLocalhostPorts = null,
        bool filterEphemeralLoopback = true)
    {
        _firewallNetworkInfo = firewallNetworkInfo;
        _allowlistHandler = new FirewallAllowlistTabHandler(validator, domainResolver, current);
        _portsHandler = new FirewallPortsTabHandler(portValidator, allowedLocalhostPorts);
        _blockedConnectionsFlow = blockedConnectionsFlow;
        _applyPresenter = applyPresenter;
        _initialAllowInternet = allowInternet;
        _initialAllowLan = allowLan;
        _initialAllowLocalhost = allowLocalhost;
        _initialFilterEphemeral = filterEphemeralLoopback;

        InitializeComponent();
        InitializeRuntimeImages();
        Icon = AppIcons.GetAppIcon();
        Text = displayName != null ? $"Internet Allowlist \u2014 {displayName}" : "Internet Allowlist";

        _tooltip.SetToolTip(_filterEphemeralCheckBox,
            "Dynamically blocks loopback connections to services running under other accounts on ephemeral ports (49152-65535).");

        _allowlistGridHelper = new FirewallAllowlistGridHelper(
            _grid,
            v => _ctxAdd.Visible = v,
            v => _ctxRemoveItem.Visible = v,
            v => _ctxExportItem.Visible = v,
            _allowlistHandler,
            (entries, title) => _importExportHelper.TryExportToFile(entries, title),
            () => _importExportHelper.TryExportCombinedToFile(),
            UpdateApplyButton, UpdateToolbarForCurrentTab);

        _portsGridHelper = new FirewallPortsGridHelper(
            _portsGrid,
            v => _portsCtxAdd.Visible = v,
            v => _portsCtxRemove.Visible = v,
            v => _portsCtxExport.Visible = v,
            _portsHandler,
            (entries, title) => _importExportHelper.TryExportToFile(entries, title),
            () => _importExportHelper.TryExportCombinedToFile(),
            UpdateApplyButton);

        _importExportHelper = new FirewallAllowlistImportExportHelper(
            importExportService, _allowlistHandler, _portsHandler,
            _allowlistGridHelper, _portsGridHelper, this);

        _allowInternetCheckBox.Checked = allowInternet;
        _allowLanCheckBox.Checked = allowLan;
        _allowLocalhostCheckBox.Checked = allowLocalhost;
        _filterEphemeralCheckBox.Checked = filterEphemeralLoopback;
        _filterEphemeralCheckBox.Enabled = !allowLocalhost;
        UpdateWarningLabel();
        UpdatePortsWarningLabel();
        _sortHelper.EnableThreeStateSorting(_grid, _allowlistGridHelper.PopulateGrid);
        _allowlistGridHelper.PopulateGrid();
        _portsGridHelper.PopulatePortsGrid();
        UpdateDnsLabel();
        UpdateApplyButton();
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

    private void OnFirewallSettingsChanged(object? sender, EventArgs e)
    {
        _filterEphemeralCheckBox.Enabled = !_allowLocalhostCheckBox.Checked;
        UpdateWarningLabel();
        UpdatePortsWarningLabel();
        UpdateApplyButton();
    }

    private void UpdateWarningLabel() =>
        _warningLabel.Visible = _allowInternetCheckBox.Checked && _allowLanCheckBox.Checked;

    private void UpdatePortsWarningLabel() =>
        _portsWarningLabel.Visible = _allowLocalhostCheckBox.Checked;

    private void UpdateDnsLabel()
    {
        try
        {
            var servers = _firewallNetworkInfo.GetDnsServerAddresses();
            _dnsLabel.Text = servers.Count > 0
                ? $"DNS servers (auto-included when allowlist is non-empty): {string.Join(", ", servers)}"
                : "DNS servers: none detected";
        }
        catch
        {
            _dnsLabel.Text = "DNS servers: unavailable";
        }
    }

    private void UpdateToolbarForCurrentTab()
    {
        bool isInternetTab = _tabControl.SelectedTab == _allowlistTab;

        _addButton.Enabled = !isInternetTab || !_allowlistGridHelper.IsResolvingDomains;
        _addButton.ToolTipText = isInternetTab
            ? "Add entry (IP, CIDR, or domain — auto-detected)"
            : "Add port exception";

        _removeButton.Enabled = isInternetTab
            ? _grid.SelectedRows.Count > 0
            : _portsGrid.SelectedRows.Count > 0;
        _removeButton.ToolTipText = isInternetTab ? "Remove selected entries" : "Remove selected ports";

        _exportButton.ToolTipText = isInternetTab
            ? "Export selected entries to file (exports all entries and ports when nothing is selected)"
            : "Export selected ports to file (exports all entries and ports when nothing is selected)";

        _resolveButton.Enabled = isInternetTab && !_allowlistGridHelper.IsResolvingDomains;
        _viewBlockedButton.Enabled = isInternetTab;
    }

    private void OnTabChanged(object? sender, EventArgs e) => UpdateToolbarForCurrentTab();

    private void OnGridSelectionChanged(object? sender, EventArgs e)
    {
        if (_tabControl.SelectedTab == _allowlistTab)
            _removeButton.Enabled = _grid.SelectedRows.Count > 0;
    }

    private void OnPortsGridSelectionChanged(object? sender, EventArgs e)
    {
        if (_tabControl.SelectedTab == _portsTab)
            _removeButton.Enabled = _portsGrid.SelectedRows.Count > 0;
    }

    private void OnAddButtonClick(object? sender, EventArgs e)
    {
        if (_tabControl.SelectedTab == _allowlistTab)
        {
            var input = PromptInput("Add Entry", "Enter an IP address, CIDR range, or domain name:");
            if (input != null)
                _allowlistGridHelper.AddEntry(input);
        }
        else
        {
            var input = PromptInput("Add Port Exception", "Enter a port number or range (e.g. 53, 8080-8090):");
            if (input != null)
                _portsGridHelper.AddPort(input);
        }
    }

    private void OnRemoveButtonClick(object? sender, EventArgs e)
    {
        if (_tabControl.SelectedTab == _allowlistTab)
            _allowlistGridHelper.RemoveSelected();
        else
            _portsGridHelper.RemoveSelected();
    }

    private void OnExportButtonClick(object? sender, EventArgs e)
    {
        if (_tabControl.SelectedTab == _allowlistTab)
            _allowlistGridHelper.ExportSelected();
        else
            _portsGridHelper.ExportSelected();
    }

    private void OnAddClick(object? sender, EventArgs e)
    {
        var input = PromptInput("Add Entry", "Enter an IP address, CIDR range, or domain name:");
        if (input != null)
            _allowlistGridHelper.AddEntry(input);
    }

    private void OnRemoveClick(object? sender, EventArgs e) => _allowlistGridHelper.RemoveSelected();
    private void OnExportClick(object? sender, EventArgs e) => _allowlistGridHelper.ExportSelected();

    private void OnGridCellEndEdit(object? sender, DataGridViewCellEventArgs e) =>
        _allowlistGridHelper.ApplyCellEdit(e.RowIndex, e.ColumnIndex);

    private void OnGridMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
            _allowlistGridHelper.HandleMouseDown(e.X, e.Y);
    }

    private void OnContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e) =>
        _allowlistGridHelper.ConfigureContextMenu();

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (_allowlistGridHelper.HandleKeyDown(e.KeyCode, e.Control))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void OnPortsAddClick(object? sender, EventArgs e)
    {
        var input = PromptInput("Add Port Exception", "Enter a port number or range (e.g. 53, 8080-8090):");
        if (input != null)
            _portsGridHelper.AddPort(input);
    }

    private void OnPortsRemoveClick(object? sender, EventArgs e) => _portsGridHelper.RemoveSelected();
    private void OnPortsExportClick(object? sender, EventArgs e) => _portsGridHelper.ExportSelected();

    private void OnPortsGridCellEndEdit(object? sender, DataGridViewCellEventArgs e) =>
        _portsGridHelper.ApplyCellEdit(e.RowIndex, e.ColumnIndex);

    private void OnPortsGridMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
            _portsGridHelper.HandleMouseDown(e.X, e.Y);
    }

    private void OnPortsContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e) =>
        _portsGridHelper.ConfigureContextMenu();

    private void OnPortsGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (_portsGridHelper.HandleKeyDown(e.KeyCode))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void OnImportClick(object? sender, EventArgs e)
    {
        _importExportHelper.OnImportClick();
        UpdateApplyButton();
    }

    /// <summary>
    /// Schedules the blocked-connections dialog to open automatically when this dialog is first shown.
    /// Passes <c>enableAuditLogging: true</c> so audit logging is activated immediately.
    /// Call this before <see cref="Form.ShowDialog()"/> — intended for the post-wizard flow.
    /// </summary>
    public void AutoOpenBlockedConnectionsOnShow()
    {
        Shown += OnAutoOpenBlockedConnections;
    }

    private void OnAutoOpenBlockedConnections(object? sender, EventArgs e)
    {
        Shown -= OnAutoOpenBlockedConnections;
        OpenBlockedConnectionsDialog(enableAuditLogging: true);
    }

    private void OnViewBlockedClick(object? sender, EventArgs e) => OpenBlockedConnectionsDialog();

    private void OpenBlockedConnectionsDialog(bool enableAuditLogging = false)
    {
        var selected = _blockedConnectionsFlow.ShowDialog(
            _allowlistHandler.GetEntries(), this, enableAuditLogging);
        if (selected == null)
            return;

        var addResult = _allowlistGridHelper.AddEntriesFromBlockedConnections(selected);
        if (addResult.TruncatedCount > 0)
        {
            var limitMessage = _allowlistHandler.GetLicenseLimitMessage();
            if (limitMessage != null)
                MessageBox.Show(limitMessage, "License Limit", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async void OnResolveClick(object? sender, EventArgs e)
    {
        if (!_allowlistHandler.HasDomainEntries())
        {
            MessageBox.Show("No domain entries to resolve.", "Resolve",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await _allowlistGridHelper.ResolveDomainEntriesAsync(showError: true);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        UpdateToolbarForCurrentTab();
        if (_allowlistHandler.HasDomainEntries())
            await _allowlistGridHelper.ResolveDomainEntriesAsync(showError: false);
    }

    private bool HasUnappliedChanges() =>
        _allowInternetCheckBox.Checked != _initialAllowInternet ||
        _allowLanCheckBox.Checked != _initialAllowLan ||
        _allowLocalhostCheckBox.Checked != _initialAllowLocalhost ||
        _filterEphemeralCheckBox.Checked != _initialFilterEphemeral ||
        _allowlistHandler.HasUnappliedChanges() ||
        _portsHandler.HasUnappliedChanges();

    private void UpdateApplyButton() => _applyButton.Enabled = !_isApplying && HasUnappliedChanges();

    private void OnApplyClick(object? sender, EventArgs e)
    {
        if (_isApplying)
            return;

        _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        _portsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        Result = _allowlistHandler.GetEntries().ToList();
        AllowInternet = _allowInternetCheckBox.Checked;
        AllowLan = _allowLanCheckBox.Checked;
        AllowLocalhost = _allowLocalhostCheckBox.Checked;
        AllowedLocalhostPorts = _portsHandler.GetPortEntries().ToList();
        FilterEphemeralLoopback = _filterEphemeralCheckBox.Checked;

        _isApplying = true;
        SetApplyState(enabled: false);
        try
        {
            var args = new FirewallApplyEventArgs();
            Applied?.Invoke(this, args);
            var presentation = _applyPresenter.Present(args.RolledBack, changedSettingsCount: 1);
            if (!presentation.RetainPendingInput)
            {
                _initialAllowInternet = _allowInternetCheckBox.Checked;
                _initialAllowLan = _allowLanCheckBox.Checked;
                _initialAllowLocalhost = _allowLocalhostCheckBox.Checked;
                _initialFilterEphemeral = _filterEphemeralCheckBox.Checked;
                _allowlistHandler.CommitApply();
                _portsHandler.CommitApply();
            }
        }
        finally
        {
            _isApplying = false;
            SetApplyState(enabled: true);
            UpdateApplyButton();
        }
    }

    private void OnCloseClick(object? sender, EventArgs e)
    {
        if (_isApplying)
            return;

        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_isApplying)
        {
            e.Cancel = true;
            return;
        }

        if (HasUnappliedChanges())
        {
            var result = MessageBox.Show(
                "You have unapplied changes. Discard and close?",
                "Internet Allowlist", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            if (_isApplying)
                return true;

            OnCloseClick(this, EventArgs.Empty);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private string? PromptInput(string title, string prompt)
    {
        using var dlg = new InputPromptDialog(title, prompt);
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.Value?.Trim() : null;
    }

    private void SetApplyState(bool enabled)
    {
        _applyButton.Enabled = enabled && HasUnappliedChanges();
        _closeButton.Enabled = enabled;
        _tabControl.Enabled = enabled;
        _toolStrip.Enabled = enabled;
        _allowInternetCheckBox.Enabled = enabled;
        _allowLanCheckBox.Enabled = enabled;
        _allowLocalhostCheckBox.Enabled = enabled;
        _filterEphemeralCheckBox.Enabled = enabled && !_allowLocalhostCheckBox.Checked;
    }
}
