using System.ComponentModel;
using System.Text;
using RunFence.Apps.UI;
using RunFence.Core.Models;
using RunFence.Licensing;
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
    private readonly ILicenseService _licenseService;
    private readonly List<FirewallAllowlistEntry> _entries;
    private readonly string? _sid;
    private readonly string? _displayName;
    private readonly IBlockedConnectionReader? _blockedConnectionReader;
    private readonly IDnsResolver? _dnsResolver;
    private int _ctxRowIndex = -1;
    private readonly GridSortHelper _sortHelper = new();

    /// <summary>
    /// The edited allowlist. Only meaningful after the dialog closes with DialogResult.OK.
    /// </summary>
    public List<FirewallAllowlistEntry> Result { get; private set; } = [];

    /// <summary>
    /// Whether Internet access is allowed. Only meaningful after the dialog closes with DialogResult.OK.
    /// </summary>
    public bool AllowInternet { get; private set; } = true;

    /// <summary>
    /// Whether LAN access is allowed. Only meaningful after the dialog closes with DialogResult.OK.
    /// </summary>
    public bool AllowLan { get; private set; } = true;

    /// <summary>
    /// Whether Localhost access is allowed. Only meaningful after the dialog closes with DialogResult.OK.
    /// </summary>
    public bool AllowLocalhost { get; private set; } = true;

    internal FirewallAllowlistDialog(
        List<FirewallAllowlistEntry> current,
        IFirewallNetworkInfo firewallNetworkInfo,
        ILicenseService licenseService,
        string? displayName = null,
        bool allowInternet = true,
        bool allowLan = true,
        bool allowLocalhost = true,
        string? sid = null,
        IBlockedConnectionReader? blockedConnectionReader = null,
        IDnsResolver? dnsResolver = null)
    {
        _firewallNetworkInfo = firewallNetworkInfo;
        _licenseService = licenseService;
        _sid = sid;
        _displayName = displayName;
        _blockedConnectionReader = blockedConnectionReader;
        _dnsResolver = dnsResolver;
        _entries = current.Select(e => new FirewallAllowlistEntry { Value = e.Value, IsDomain = e.IsDomain }).ToList();

        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        Text = displayName != null ? $"Internet Whitelist \u2014 {displayName}" : "Internet Whitelist";

        _allowInternetCheckBox.Checked = allowInternet;
        _allowLanCheckBox.Checked = allowLan;
        _allowLocalhostCheckBox.Checked = allowLocalhost;
        UpdateWarningLabel();
        _viewBlockedButton.Enabled = _blockedConnectionReader != null;

        _sortHelper.EnableThreeStateSorting(_grid, PopulateGrid);
        PopulateGrid();
        UpdateDnsLabel();
    }

    private void OnFirewallSettingsChanged(object? sender, EventArgs e) => UpdateWarningLabel();

    private void UpdateWarningLabel()
    {
        _warningLabel.Visible = _allowInternetCheckBox.Checked && _allowLanCheckBox.Checked;
    }

    private void PopulateGrid()
    {
        _grid.Rows.Clear();
        foreach (var entry in _entries)
            AddRow(entry);
    }

    private DataGridViewRow AddRow(FirewallAllowlistEntry entry)
    {
        var typeText = entry.IsDomain ? "Domain" : "IP/CIDR";
        var resolvedText = entry.IsDomain ? "" : entry.Value;
        var idx = _grid.Rows.Add(typeText, entry.Value, resolvedText);
        _grid.Rows[idx].Tag = entry;
        return _grid.Rows[idx];
    }

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

    private void OnGridSelectionChanged(object? sender, EventArgs e)
    {
        _removeButton.Enabled = _grid.SelectedRows.Count > 0;
    }

    private void OnGridMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
            return;
        var hit = _grid.HitTest(e.X, e.Y);
        if (hit.RowIndex >= 0)
        {
            _ctxRowIndex = hit.RowIndex;
            _grid.ClearSelection();
            _grid.Rows[hit.RowIndex].Selected = true;
        }
        else
        {
            _ctxRowIndex = -1;
            _grid.ClearSelection();
        }
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        _ctxAdd.Visible = _ctxRowIndex < 0;
        _ctxRemoveItem.Visible = _ctxRowIndex >= 0;
        _ctxExportItem.Visible = _ctxRowIndex >= 0;
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete && _grid.SelectedRows.Count > 0 && !_grid.IsCurrentCellInEditMode)
        {
            OnRemoveClick(sender, EventArgs.Empty);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void OnAddClick(object? sender, EventArgs e)
    {
        if (!CheckLicenseLimit())
            return;

        var input = PromptInput("Add Entry", "Enter an IP address, CIDR range, or domain name:");
        if (input == null)
            return;

        bool isDomain;
        if (FirewallAddressRangeBuilder.IsValidIpOrCidr(input))
            isDomain = false;
        else if (IsValidDomain(input))
            isDomain = true;
        else
        {
            MessageBox.Show(
                "Invalid entry. Enter a valid IP address or CIDR range (e.g. 1.2.3.4, 10.0.0.0/8) or domain name (e.g. example.com).",
                "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (HasDuplicate(input))
        {
            MessageBox.Show("This entry is already in the list.", "Duplicate",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var entry = new FirewallAllowlistEntry { Value = input, IsDomain = isDomain };
        _entries.Add(entry);
        var row = AddRow(entry);
        if (isDomain)
            ResolveEntryAsync(entry, row);
    }

    private bool CheckLicenseLimit()
    {
        if (_licenseService.CanAddFirewallAllowlistEntry(_entries.Count))
            return true;
        var msg = _licenseService.GetRestrictionMessage(EvaluationFeature.FirewallAllowlist, _entries.Count);
        MessageBox.Show(msg, "License Limit", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private void OnRemoveClick(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0)
            return;
        var toRemove = _grid.SelectedRows.Cast<DataGridViewRow>().ToList();
        foreach (var row in toRemove)
        {
            if (row.Tag is FirewallAllowlistEntry entry)
                _entries.Remove(entry);
            _grid.Rows.Remove(row);
        }
    }

    private void OnExportClick(object? sender, EventArgs e)
    {
        var selectedEntries = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Where(r => r.Tag is FirewallAllowlistEntry)
            .Select(r => (FirewallAllowlistEntry)r.Tag!)
            .ToList();
        var entriesToExport = selectedEntries.Count > 0 ? selectedEntries : _entries;

        if (entriesToExport.Count == 0)
        {
            MessageBox.Show("Nothing to export.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog();
        dlg.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
        dlg.DefaultExt = "txt";
        dlg.Title = "Export Firewall Allowlist";
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            File.WriteAllLines(dlg.FileName, entriesToExport.Select(entry => entry.Value), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnImportClick(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog();
        dlg.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
        dlg.Title = "Import Firewall Allowlist";
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(dlg.FileName, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var added = 0;
        var limitReached = false;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            bool isDomain;
            if (FirewallAddressRangeBuilder.IsValidIpOrCidr(line))
                isDomain = false;
            else if (IsValidDomain(line))
                isDomain = true;
            else
                continue;

            if (HasDuplicate(line))
                continue;

            if (!_licenseService.CanAddFirewallAllowlistEntry(_entries.Count))
            {
                limitReached = true;
                break;
            }

            var entry = new FirewallAllowlistEntry { Value = line, IsDomain = isDomain };
            _entries.Add(entry);
            var row = AddRow(entry);
            if (isDomain)
                ResolveEntryAsync(entry, row);
            added++;
        }

        if (limitReached)
        {
            var msg = _licenseService.GetRestrictionMessage(EvaluationFeature.FirewallAllowlist, _entries.Count);
            MessageBox.Show($"Import stopped: license limit reached.\n\n{msg}", "License Limit",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else if (added == 0)
        {
            MessageBox.Show("No new entries to import (all duplicates or invalid).", "Import",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show($"Imported {added} {(added == 1 ? "entry" : "entries")}.", "Import",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_entries.Any(en => en.IsDomain))
            await ResolveDomainEntriesAsync(showError: false);
    }

    private async void OnResolveClick(object? sender, EventArgs e)
    {
        if (!_entries.Any(en => en.IsDomain))
        {
            MessageBox.Show("No domain entries to resolve.", "Resolve",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await ResolveDomainEntriesAsync(showError: true);
    }

    private async Task ResolveDomainEntriesAsync(bool showError)
    {
        _resolveButton.Enabled = false;
        _addButton.Enabled = false;
        SetDomainRowsResolvedText("Resolving...");

        try
        {
            var resolved = await _firewallNetworkInfo.ResolveDomainEntriesAsync(_entries);
            UpdateResolvedColumn(resolved);
        }
        catch (Exception ex)
        {
            if (showError)
                MessageBox.Show($"Resolution failed: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetDomainRowsResolvedText("(unresolved)");
        }
        finally
        {
            _resolveButton.Enabled = true;
            _addButton.Enabled = true;
        }
    }

    private void OnGridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
            return;
        if (_grid.Columns[e.ColumnIndex].Name != "Value")
            return;
        var row = _grid.Rows[e.RowIndex];
        if (row.Tag is not FirewallAllowlistEntry entry)
            return;

        var newValue = (row.Cells[e.ColumnIndex].Value as string)?.Trim() ?? "";
        if (string.Equals(newValue, entry.Value, StringComparison.OrdinalIgnoreCase))
            return;

        bool isDomain;
        if (FirewallAddressRangeBuilder.IsValidIpOrCidr(newValue))
            isDomain = false;
        else if (IsValidDomain(newValue))
            isDomain = true;
        else
        {
            MessageBox.Show(
                "Invalid entry. Enter a valid IP address or CIDR range (e.g. 1.2.3.4, 10.0.0.0/8) or domain name (e.g. example.com).",
                "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            row.Cells[e.ColumnIndex].Value = entry.Value;
            return;
        }

        if (_entries.Any(en => en != entry && string.Equals(en.Value, newValue, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("This value is already in the list.", "Duplicate",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            row.Cells[e.ColumnIndex].Value = entry.Value;
            return;
        }

        entry.Value = newValue;
        entry.IsDomain = isDomain;
        row.Cells[e.ColumnIndex].Value = newValue;
        row.Cells["Type"].Value = isDomain ? "Domain" : "IP/CIDR";
        if (isDomain)
            ResolveEntryAsync(entry, row);
        else
            row.Cells["Resolved"].Value = newValue;
    }

    private async void ResolveEntryAsync(FirewallAllowlistEntry entry, DataGridViewRow row)
    {
        row.Cells["Resolved"].Value = "Resolving...";
        try
        {
            var resolved = await _firewallNetworkInfo.ResolveDomainEntriesAsync([entry]);
            if (row.DataGridView == null)
                return;
            row.Cells["Resolved"].Value = resolved.TryGetValue(entry.Value, out var ips) && ips.Count > 0
                ? string.Join(", ", ips)
                : "(no addresses)";
        }
        catch
        {
            if (row.DataGridView == null)
                return;
            row.Cells["Resolved"].Value = "(unresolved)";
        }
    }

    private void SetDomainRowsResolvedText(string text)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is FirewallAllowlistEntry { IsDomain: true })
                row.Cells["Resolved"].Value = text;
        }
    }

    private void UpdateResolvedColumn(Dictionary<string, List<string>> resolved)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not FirewallAllowlistEntry entry || !entry.IsDomain)
                continue;
            row.Cells["Resolved"].Value = resolved.TryGetValue(entry.Value, out var ips) && ips.Count > 0
                ? string.Join(", ", ips)
                : "(no addresses)";
        }
    }

    private void OnViewBlockedClick(object? sender, EventArgs e)
    {
        if (_blockedConnectionReader == null || _dnsResolver == null)
            return;

        using var dlg = new BlockedConnectionsDialog(
            _displayName ?? _sid ?? "", _blockedConnectionReader, _dnsResolver, _entries.AsReadOnly());
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        foreach (var entry in dlg.SelectedEntries)
        {
            if (HasDuplicate(entry.Value))
                continue;
            _entries.Add(entry);
            var row = AddRow(entry);
            if (entry.IsDomain)
                ResolveEntryAsync(entry, row);
        }
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            OnCancelClick(this, EventArgs.Empty);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        Result = _entries.ToList();
        AllowInternet = _allowInternetCheckBox.Checked;
        AllowLan = _allowLanCheckBox.Checked;
        AllowLocalhost = _allowLocalhostCheckBox.Checked;
        DialogResult = DialogResult.OK;
        Close();
    }

    private string? PromptInput(string title, string prompt)
    {
        using var dlg = new InputPromptDialog(title, prompt);
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.Value?.Trim() : null;
    }

    private bool HasDuplicate(string value)
        => _entries.Any(en => string.Equals(en.Value, value, StringComparison.OrdinalIgnoreCase));

    private static bool IsValidDomain(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (value.Length > 253)
            return false;
        foreach (var label in value.Split('.'))
        {
            if (label.Length is 0 or > 63)
                return false;
            foreach (var ch in label)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '-')
                    return false;
            }

            if (label[0] == '-' || label[^1] == '-')
                return false;
        }

        return true;
    }
}