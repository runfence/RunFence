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
    FirewallApplyHelper firewallApplyHelper,
    FirewallDialogFactory firewallDialogFactory)
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
            existing.AllowInternet = dialog.AllowInternet;
            existing.AllowLan = dialog.AllowLan;
            existing.AllowLocalhost = dialog.AllowLocalhost;
            existing.LocalhostPortExemptions = dialog.AllowedLocalhostPorts.ToList();
            existing.FilterEphemeralLoopback = dialog.FilterEphemeralLoopback;
            existing.Allowlist = dialog.Result;
            FirewallAccountSettings.UpdateOrRemove(db, accountRow.Sid, existing);

            sessionSaver.SaveConfig();
            SaveAndRefreshRequested?.Invoke();

            var finalSettings = db.GetAccount(accountRow.Sid)?.Firewall ?? new FirewallAccountSettings();
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
