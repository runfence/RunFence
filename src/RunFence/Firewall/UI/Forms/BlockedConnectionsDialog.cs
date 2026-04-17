using System.Collections.Concurrent;
using RunFence.Apps.UI;
using RunFence.Core.Models;
using RunFence.UI;

namespace RunFence.Firewall.UI.Forms;

public partial class BlockedConnectionsDialog : Form
{
    private readonly IBlockedConnectionReader _reader;
    private readonly IDnsResolver _dnsResolver;
    private readonly BlockedConnectionAggregator _aggregator;
    private readonly IReadOnlyList<FirewallAllowlistEntry> _existingAllowlist;
    private readonly bool _forceEnableAuditLogging;
    private List<BlockedConnectionRow> _rows = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _reverseDnsMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly GridSortHelper _sortHelper = new();

    public List<FirewallAllowlistEntry> SelectedEntries { get; private set; } = new();

    public BlockedConnectionsDialog(
        IBlockedConnectionReader reader,
        IDnsResolver dnsResolver,
        BlockedConnectionAggregator aggregator,
        IReadOnlyList<FirewallAllowlistEntry> existingAllowlist,
        bool enableAuditLogging = false)
    {
        _reader = reader;
        _dnsResolver = dnsResolver;
        _aggregator = aggregator;
        _existingAllowlist = existingAllowlist;
        _forceEnableAuditLogging = enableAuditLogging;

        InitializeComponent();
        Icon = AppIcons.GetAppIcon();
        _sortHelper.EnableThreeStateSorting(_grid, PopulateGrid);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        bool currentPolicy = await Task.Run(TryGetAuditPolicy);
        bool shouldEnable = _forceEnableAuditLogging || currentPolicy;
        _auditCheckBox.Checked = shouldEnable;
        // If pre-enabling from wizard and not already enabled, apply the policy now.
        if (shouldEnable && !currentPolicy)
        {
            try
            {
                await Task.Run(() => _reader.SetAuditPolicyEnabled(true));
            }
            catch
            {
            }
        }

        _auditCheckBox.CheckedChanged += OnAuditCheckBoxChanged;
        await LoadConnectionsAsync();
    }

    private async Task LoadConnectionsAsync()
    {
        SetLoadingState(true);
        try
        {
            var connections = await Task.Run(() => _reader.ReadBlockedConnections(TimeSpan.FromHours(24)));
            _rows = _aggregator.AggregateByAddress(connections);
            _reverseDnsMap.Clear();
            // AggregateByAddress already sorts by LastSeen descending — no secondary sort needed.
            PopulateGrid();
            _ = ResolveDnsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to read blocked connections: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetLoadingState(false);
        }
    }

    private void PopulateGrid()
    {
        _grid.Rows.Clear();
        foreach (var row in _rows)
        {
            var idx = _grid.Rows.Add(
                row.IpAddress,
                _reverseDnsMap.TryGetValue(row.IpAddress, out var hosts) ? string.Join(", ", hosts) : "",
                row.HitCount,
                row.LastSeen.ToLocalTime().ToString("g"),
                string.Join(", ", row.Ports));

            var gridRow = _grid.Rows[idx];
            gridRow.Tag = row;

            if (IsInAllowlist(row.IpAddress))
                gridRow.DefaultCellStyle.ForeColor = SystemColors.GrayText;
        }
    }

    private async Task ResolveDnsAsync()
    {
        var ips = _rows.Select(r => r.IpAddress).ToList();
        using var sem = new SemaphoreSlim(10);

        var tasks = ips.Select(async ip =>
        {
            await sem.WaitAsync();
            try
            {
                var hostnames = await _dnsResolver.ResolveReverseAsync(ip);
                _reverseDnsMap[ip] = hostnames;
                try
                {
                    if (!IsDisposed)
                        BeginInvoke(() => UpdateHostnameCell(ip, hostnames));
                }
                catch (ObjectDisposedException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }
            finally
            {
                sem.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private void UpdateHostnameCell(string ip, IReadOnlyList<string> hostnames)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is BlockedConnectionRow bcr && bcr.IpAddress == ip)
            {
                row.Cells["colHostname"].Value = string.Join(", ", hostnames);
                break;
            }
        }
    }

    private bool IsInAllowlist(string ip) =>
        _existingAllowlist.Any(e => string.Equals(e.Value, ip, StringComparison.OrdinalIgnoreCase));

    private void SetLoadingState(bool loading)
    {
        _refreshButton.Enabled = !loading;
        _addSelectedButton.Enabled = !loading;
        if (loading)
            _grid.Rows.Clear();
    }

    private bool TryGetAuditPolicy()
    {
        try
        {
            return _reader.IsAuditPolicyEnabled();
        }
        catch
        {
            return false;
        }
    }

    private async void OnAuditCheckBoxChanged(object? sender, EventArgs e)
    {
        _auditCheckBox.Enabled = false;
        var wantEnabled = _auditCheckBox.Checked;
        try
        {
            await Task.Run(() => _reader.SetAuditPolicyEnabled(wantEnabled));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to change audit policy: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            _auditCheckBox.CheckedChanged -= OnAuditCheckBoxChanged;
            _auditCheckBox.Checked = !wantEnabled;
            _auditCheckBox.CheckedChanged += OnAuditCheckBoxChanged;
        }
        finally
        {
            _auditCheckBox.Enabled = true;
        }
    }

    private async void OnRefreshClick(object? sender, EventArgs e)
    {
        await LoadConnectionsAsync();
    }

    private void OnAddSelectedClick(object? sender, EventArgs e)
    {
        var selectedRows = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Where(r => r.Tag is BlockedConnectionRow && !IsInAllowlist(((BlockedConnectionRow)r.Tag!).IpAddress))
            .Select(r => (BlockedConnectionRow)r.Tag!)
            .ToList();

        if (selectedRows.Count == 0)
        {
            MessageBox.Show("No entries selected.", "Add Selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        bool isDomainMode = _domainRadio.Checked;
        SelectedEntries = _aggregator.BuildAllowlistEntries(selectedRows, isDomainMode, _reverseDnsMap);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnCloseClick(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
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
}