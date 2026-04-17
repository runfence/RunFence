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
public partial class FirewallAllowlistDialog : Form
{
    private readonly IFirewallNetworkInfo _firewallNetworkInfo;
    private readonly FirewallAllowlistTabHandler _allowlistHandler;
    private readonly FirewallPortsTabHandler _portsHandler;
    private readonly FirewallAllowlistGridHelper _allowlistGridHelper;
    private readonly FirewallPortsGridHelper _portsGridHelper;
    private readonly BlockedConnectionAggregator _aggregator;
    private readonly FirewallAllowlistImportExportService _importExportService;
    private readonly IBlockedConnectionReader? _blockedConnectionReader;
    private readonly IDnsResolver? _dnsResolver;
    private bool _initialAllowInternet;
    private bool _initialAllowLan;
    private bool _initialAllowLocalhost;
    private bool _initialFilterEphemeral;
    private readonly GridSortHelper _sortHelper = new();

    /// <summary>
    /// Raised on each Apply click, after dialog properties are updated.
    /// Subscribers should save settings to the database and apply firewall rules immediately.
    /// Set <see cref="FirewallApplyEventArgs.RolledBack"/> to <c>true</c> when settings were
    /// rolled back after a failure, so the dialog keeps the Apply button enabled for a retry.
    /// </summary>
    public event EventHandler<FirewallApplyEventArgs>? Applied;

    /// <summary>
    /// True if the user applied changes at least once. Only meaningful after the dialog closes.
    /// </summary>
    public bool WasApplied { get; private set; }

    /// <summary>
    /// The last applied allowlist. Only meaningful when <see cref="WasApplied"/> is true.
    /// </summary>
    public List<FirewallAllowlistEntry> Result { get; private set; } = [];

    /// <summary>
    /// Whether Internet access is allowed. Only meaningful when <see cref="WasApplied"/> is true.
    /// </summary>
    public bool AllowInternet { get; private set; } = true;

    /// <summary>
    /// Whether LAN access is allowed. Only meaningful when <see cref="WasApplied"/> is true.
    /// </summary>
    public bool AllowLan { get; private set; } = true;

    /// <summary>
    /// Whether Localhost access is allowed. Only meaningful when <see cref="WasApplied"/> is true.
    /// </summary>
    public bool AllowLocalhost { get; private set; } = true;

    /// <summary>
    /// The last applied localhost port allow-list. Only meaningful when <see cref="WasApplied"/> is true.
    /// </summary>
    public IReadOnlyList<string> AllowedLocalhostPorts { get; private set; } = [];

    /// <summary>
    /// Whether the background scanner is enabled to block cross-user ephemeral ports.
    /// Only meaningful when <see cref="WasApplied"/> is true.
    /// </summary>
    public bool FilterEphemeralLoopback { get; private set; } = true;

    internal FirewallAllowlistDialog(
        List<FirewallAllowlistEntry> current,
        IFirewallNetworkInfo firewallNetworkInfo,
        FirewallAllowlistValidator validator,
        FirewallPortValidator portValidator,
        FirewallDomainResolver domainResolver,
        BlockedConnectionAggregator aggregator,
        FirewallAllowlistImportExportService importExportService,
        string? displayName = null,
        bool allowInternet = true,
        bool allowLan = true,
        bool allowLocalhost = true,
        IBlockedConnectionReader? blockedConnectionReader = null,
        IDnsResolver? dnsResolver = null,
        IReadOnlyList<string>? allowedLocalhostPorts = null,
        bool filterEphemeralLoopback = true)
    {
        _firewallNetworkInfo = firewallNetworkInfo;
        _allowlistHandler = new FirewallAllowlistTabHandler(validator, domainResolver, current);
        _portsHandler = new FirewallPortsTabHandler(portValidator, allowedLocalhostPorts);
        _aggregator = aggregator;
        _importExportService = importExportService;
        _blockedConnectionReader = blockedConnectionReader;
        _dnsResolver = dnsResolver;
        _initialAllowInternet = allowInternet;
        _initialAllowLan = allowLan;
        _initialAllowLocalhost = allowLocalhost;
        _initialFilterEphemeral = filterEphemeralLoopback;

        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        Text = displayName != null ? $"Internet Allowlist \u2014 {displayName}" : "Internet Allowlist";

        _allowlistGridHelper = new FirewallAllowlistGridHelper(
            _grid, _ctxAdd, _ctxRemoveItem, _ctxExportItem,
            _allowlistHandler,
            TryExportToFile, TryExportCombinedToFile,
            UpdateApplyButton, UpdateToolbarForCurrentTab);

        _portsGridHelper = new FirewallPortsGridHelper(
            _portsGrid, _portsCtxAdd, _portsCtxRemove, _portsCtxExport,
            _portsHandler,
            TryExportToFile, TryExportCombinedToFile,
            UpdateApplyButton);

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
        _viewBlockedButton.Enabled = isInternetTab && _blockedConnectionReader != null;
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
            _allowlistGridHelper.RemoveSelectedEntries();
        else
            _portsGridHelper.RemoveSelectedPorts();
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

    private void OnRemoveClick(object? sender, EventArgs e) => _allowlistGridHelper.RemoveSelectedEntries();
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

    private void OnPortsRemoveClick(object? sender, EventArgs e) => _portsGridHelper.RemoveSelectedPorts();
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
        using var dlg = new OpenFileDialog();
        dlg.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
        dlg.Title = "Import Firewall Settings";
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        var fileResult = _importExportService.ImportFromFile(dlg.FileName);
        if (fileResult?.Lines == null)
        {
            MessageBox.Show($"Import failed: {fileResult?.ErrorMessage ?? "Unknown error"}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var allowlistResult = _allowlistHandler.ImportLines(fileResult.Lines.AllowlistLines);
        var portsResult = _portsHandler.ImportLines(fileResult.Lines.PortLines);

        if (allowlistResult.AddedEntries.Count == 0 && portsResult.AddedPorts.Count == 0)
        {
            MessageBox.Show("No new entries to import (all duplicates or invalid).", "Import",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _allowlistGridHelper.AddImportedEntries(allowlistResult.AddedEntries);
        _portsGridHelper.AddImportedPorts(portsResult.AddedPorts);

        var parts = new List<string>();
        if (allowlistResult.AddedEntries.Count > 0)
            parts.Add($"{allowlistResult.AddedEntries.Count} {(allowlistResult.AddedEntries.Count == 1 ? "allowlist entry" : "allowlist entries")}");
        if (portsResult.AddedPorts.Count > 0)
            parts.Add($"{portsResult.AddedPorts.Count} {(portsResult.AddedPorts.Count == 1 ? "port exception" : "port exceptions")}");
        var msg = $"Imported {string.Join(" and ", parts)}.";
        if (allowlistResult.EntryLimitReached)
            msg += $"\n\n{allowlistResult.LicenseLimitMessage}";
        if (portsResult.PortLimitReached)
            msg += $"\n\nMaximum of {LocalhostPortParser.MaxAllowedPorts} port entries reached.";
        MessageBox.Show(msg, "Import", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        if (_blockedConnectionReader == null || _dnsResolver == null)
            return;

        using var dlg = new BlockedConnectionsDialog(
            _blockedConnectionReader, _dnsResolver,
            _aggregator, _allowlistHandler.GetEntries(),
            enableAuditLogging: enableAuditLogging);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        var addResult = _allowlistGridHelper.AddEntriesFromBlockedConnections(dlg.SelectedEntries);
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

    private bool TryExportToFile(IReadOnlyList<string> entries, string title)
    {
        if (entries.Count == 0)
        {
            MessageBox.Show("Nothing to export.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        using var dlg = new SaveFileDialog();
        dlg.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
        dlg.DefaultExt = "txt";
        dlg.Title = title;
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return false;

        var exportEntries = entries.Select(v => new FirewallAllowlistEntry { Value = v }).ToList();
        var error = _importExportService.ExportToFile(dlg.FileName, exportEntries);
        if (error != null)
        {
            MessageBox.Show($"Export failed: {error}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        return true;
    }

    private void TryExportCombinedToFile()
    {
        var allEntries = _allowlistHandler.GetEntries();
        var allPorts = _portsHandler.GetPortEntries();
        if (allEntries.Count == 0 && allPorts.Count == 0)
        {
            MessageBox.Show("Nothing to export.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog();
        dlg.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
        dlg.DefaultExt = "txt";
        dlg.Title = "Export Firewall Settings";
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        var error = _importExportService.ExportCombinedToFile(dlg.FileName, allEntries, allPorts);
        if (error != null)
            MessageBox.Show($"Export failed: {error}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private bool HasUnappliedChanges() =>
        _allowInternetCheckBox.Checked != _initialAllowInternet ||
        _allowLanCheckBox.Checked != _initialAllowLan ||
        _allowLocalhostCheckBox.Checked != _initialAllowLocalhost ||
        _filterEphemeralCheckBox.Checked != _initialFilterEphemeral ||
        _allowlistHandler.HasUnappliedChanges() ||
        _portsHandler.HasUnappliedChanges();

    private void UpdateApplyButton() => _applyButton.Enabled = HasUnappliedChanges();

    private void OnApplyClick(object? sender, EventArgs e)
    {
        _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        _portsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        Result = _allowlistHandler.GetEntries().ToList();
        AllowInternet = _allowInternetCheckBox.Checked;
        AllowLan = _allowLanCheckBox.Checked;
        AllowLocalhost = _allowLocalhostCheckBox.Checked;
        AllowedLocalhostPorts = _portsHandler.GetPortEntries().ToList();
        FilterEphemeralLoopback = _filterEphemeralCheckBox.Checked;

        WasApplied = true;
        var args = new FirewallApplyEventArgs();
        Applied?.Invoke(this, args);
        if (!args.RolledBack)
        {
            _initialAllowInternet = _allowInternetCheckBox.Checked;
            _initialAllowLan = _allowLanCheckBox.Checked;
            _initialAllowLocalhost = _allowLocalhostCheckBox.Checked;
            _initialFilterEphemeral = _filterEphemeralCheckBox.Checked;
            _allowlistHandler.CommitApply();
            _portsHandler.CommitApply();
        }
        UpdateApplyButton();
    }

    private void OnCloseClick(object? sender, EventArgs e) => Close();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
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
}
