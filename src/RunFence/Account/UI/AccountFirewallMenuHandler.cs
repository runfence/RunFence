using RunFence.Core.Models;
using RunFence.Firewall.UI;
using RunFence.Firewall.UI.Forms;
using RunFence.Infrastructure;

namespace RunFence.Account.UI;

/// <summary>
/// Handles the firewall allowlist dialog for accounts in the accounts grid.
/// </summary>
public class AccountFirewallMenuHandler(
    ISessionProvider sessionProvider,
    ISessionSaver sessionSaver,
    IFirewallApplyHelper firewallApplyHelper,
    IFirewallDialogFactory firewallDialogFactory)
{
    private DataGridView _grid = null!;

    public event Action? SaveAndRefreshRequested;

    public bool IsAvailable => firewallDialogFactory.IsAvailable;

    public void Initialize(DataGridView grid)
    {
        _grid = grid;
    }

    public void OpenFirewallAllowlist(AccountRow accountRow)
    {
        if (string.IsNullOrEmpty(accountRow.Sid))
            return;

        var session = sessionProvider.GetSession();
        var db = session.Database;
        var settings = db.GetAccount(accountRow.Sid)?.Firewall ?? new FirewallAccountSettings();
        var currentAllowlist = settings.Allowlist.ToList();

        var owner = _grid.FindForm();
        using var dialog = firewallDialogFactory.CreateAllowlistDialog(
            current: currentAllowlist,
            displayName: accountRow.Username,
            allowInternet: settings.AllowInternet,
            allowLan: settings.AllowLan,
            allowLocalhost: settings.AllowLocalhost,
            allowedLocalhostPorts: settings.LocalhostPortExemptions,
            filterEphemeralLoopback: settings.FilterEphemeralLoopback);

        if (dialog == null)
            return;

        dialog.Applied += (_, args) =>
        {
            var existing = db.GetAccount(accountRow.Sid)?.Firewall ?? new FirewallAccountSettings();
            var previousSettings = existing.Clone();
            var finalSettings = new FirewallAccountSettings
            {
                AllowInternet = dialog.AllowInternet,
                AllowLan = dialog.AllowLan,
                AllowLocalhost = dialog.AllowLocalhost,
                LocalhostPortExemptions = dialog.AllowedLocalhostPorts.ToList(),
                FilterEphemeralLoopback = dialog.FilterEphemeralLoopback,
                Allowlist = dialog.Result
            };
            var username = db.SidNames.GetValueOrDefault(accountRow.Sid) ?? accountRow.Username;
            bool rolledBack = firewallApplyHelper.ApplyWithRollback(
                owner: owner,
                sid: accountRow.Sid,
                username: username,
                previous: previousSettings,
                final: finalSettings,
                database: db,
                saveAction: () => { sessionSaver.SaveConfig(); SaveAndRefreshRequested?.Invoke(); });
            if (rolledBack)
                args.RolledBack = true;
        };

        dialog.ShowDialog(owner);
    }
}
