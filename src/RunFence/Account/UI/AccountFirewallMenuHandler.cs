using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.UI.Forms;
using RunFence.Infrastructure;
using RunFence.Licensing;

namespace RunFence.Account.UI;

/// <summary>
/// Handles the firewall allowlist dialog for accounts in the accounts grid.
/// </summary>
public class AccountFirewallMenuHandler
{
    private readonly ISessionProvider _sessionProvider;
    private readonly ISessionSaver _sessionSaver;
    private readonly IFirewallService _firewallService;
    private readonly IFirewallNetworkInfo _firewallNetworkInfo;
    private readonly IBlockedConnectionReader _blockedConnectionReader;
    private readonly IDnsResolver _dnsResolver;
    private readonly ILicenseService _licenseService;

    private DataGridView _grid = null!;

    public event Action? SaveAndRefreshRequested;

    public AccountFirewallMenuHandler(
        ISessionProvider sessionProvider,
        ISessionSaver sessionSaver,
        IFirewallService firewallService,
        ILicenseService licenseService,
        IFirewallNetworkInfo firewallNetworkInfo,
        IBlockedConnectionReader blockedConnectionReader,
        IDnsResolver dnsResolver)
    {
        _sessionProvider = sessionProvider;
        _sessionSaver = sessionSaver;
        _firewallService = firewallService;
        _licenseService = licenseService;
        _firewallNetworkInfo = firewallNetworkInfo;
        _blockedConnectionReader = blockedConnectionReader;
        _dnsResolver = dnsResolver;
    }

    public bool IsAvailable => true;

    public void Initialize(DataGridView grid)
    {
        _grid = grid;
    }

    public async void OpenFirewallAllowlist(AccountRow accountRow)
    {
        if (string.IsNullOrEmpty(accountRow.Sid))
            return;

        var session = _sessionProvider.GetSession();
        var db = session.Database;
        var settings = db.GetAccount(accountRow.Sid)?.Firewall ?? new FirewallAccountSettings();
        var currentAllowlist = settings.Allowlist.ToList();

        var owner = _grid.FindForm();
        using var dialog = new FirewallAllowlistDialog(
            currentAllowlist, _firewallNetworkInfo, _licenseService,
            displayName: accountRow.Username,
            allowInternet: settings.AllowInternet, allowLan: settings.AllowLan,
            allowLocalhost: settings.AllowLocalhost,
            sid: accountRow.Sid,
            blockedConnectionReader: _blockedConnectionReader,
            dnsResolver: _dnsResolver);
        if (dialog.ShowDialog(owner) != DialogResult.OK)
            return;

        var existing = db.GetAccount(accountRow.Sid)?.Firewall ?? new FirewallAccountSettings();
        existing.AllowInternet = dialog.AllowInternet;
        existing.AllowLan = dialog.AllowLan;
        existing.AllowLocalhost = dialog.AllowLocalhost;
        existing.Allowlist = dialog.Result;
        FirewallAccountSettings.UpdateOrRemove(db, accountRow.Sid, existing);

        _sessionSaver.SaveConfig();
        SaveAndRefreshRequested?.Invoke();

        var finalSettings = db.GetAccount(accountRow.Sid)?.Firewall ?? new FirewallAccountSettings();
        var username = db.SidNames.GetValueOrDefault(accountRow.Sid) ?? accountRow.Username;
        // Pre-resolve domain allowlist entries so ApplyFirewallRules doesn't block the UI thread.
        IReadOnlyDictionary<string, IReadOnlyList<string>>? preResolved = null;
        if (finalSettings.Allowlist.Any(e => e.IsDomain))
        {
            try
            {
                var resolved = await _firewallNetworkInfo.ResolveDomainEntriesAsync(finalSettings.Allowlist);
                preResolved = resolved.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (IReadOnlyList<string>)kvp.Value,
                    StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                /* fallback to synchronous resolution inside ApplyFirewallRules */
            }
        }

        _firewallService.ApplyFirewallRules(accountRow.Sid, username, finalSettings, preResolved);
    }
}