using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.UI.Forms;
using RunFence.Licensing;
using RunFence.Persistence;

namespace RunFence.Wizard.Templates;

/// <summary>
/// Handles firewall operations for the AI agent wizard template:
/// applying restrictive rules after package installation and building the
/// post-wizard action that opens the allowlist and blocked-connections dialogs.
/// </summary>
public class AiAgentFirewallOrchestrator(
    IFirewallService firewallService,
    ILicenseService licenseService,
    IFirewallNetworkInfo? firewallNetworkInfo,
    IBlockedConnectionReader? blockedConnectionReader,
    IDnsResolver? dnsResolver,
    IDatabaseProvider databaseProvider)
{
    /// <summary>
    /// Updates firewall settings in the database and applies rules via <see cref="IFirewallService"/>.
    /// Reports progress and non-fatal errors via <paramref name="progress"/>.
    /// </summary>
    public async Task ApplyRestrictiveRulesAsync(
        string sid,
        string username,
        FirewallAccountSettings settings,
        IWizardProgressReporter progress)
    {
        var database = databaseProvider.GetDatabase();
        FirewallAccountSettings.UpdateOrRemove(database, sid, settings);
        progress.ReportStatus("Applying firewall rules...");
        try
        {
            await Task.Run(() => firewallService.ApplyFirewallRules(sid, username, settings));
        }
        catch (Exception ex)
        {
            progress.ReportError($"Firewall rules: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the post-wizard action that opens the firewall allowlist dialog followed by the
    /// blocked-connections dialog, allowing the user to whitelist required domains immediately.
    /// Returns <c>null</c> when firewall network info is unavailable (firewall not configured).
    /// </summary>
    public Action<IWin32Window>? BuildPostWizardAction(
        string sid,
        string username,
        SessionContext session,
        IWizardSessionSaver sessionSaver)
    {
        if (firewallNetworkInfo == null)
            return null;

        return owner =>
        {
            var currentSettings = session.Database.GetAccount(sid)?.Firewall
                                  ?? new FirewallAccountSettings();

            using var allowlistDlg = new FirewallAllowlistDialog(
                current: currentSettings.Allowlist.ToList(),
                firewallNetworkInfo: firewallNetworkInfo,
                displayName: username,
                allowInternet: currentSettings.AllowInternet,
                allowLan: currentSettings.AllowLan,
                allowLocalhost: currentSettings.AllowLocalhost,
                licenseService: licenseService,
                sid: sid,
                blockedConnectionReader: blockedConnectionReader,
                dnsResolver: dnsResolver);

            if (allowlistDlg.ShowDialog(owner) == DialogResult.OK)
            {
                var existing = session.Database.GetAccount(sid)?.Firewall
                               ?? new FirewallAccountSettings();
                existing.AllowInternet = allowlistDlg.AllowInternet;
                existing.AllowLan = allowlistDlg.AllowLan;
                existing.AllowLocalhost = allowlistDlg.AllowLocalhost;
                existing.Allowlist = allowlistDlg.Result;
                FirewallAccountSettings.UpdateOrRemove(session.Database, sid, existing);
                sessionSaver.SaveAndRefresh();
                var finalSettings = session.Database.GetAccount(sid)?.Firewall ?? new FirewallAccountSettings();
                firewallService.ApplyFirewallRules(sid, username, finalSettings);
            }

            // Open blocked connections dialog so the user can audit what the AI agent is
            // trying to reach and quickly add allowlist entries from real connection data.
            if (blockedConnectionReader != null && dnsResolver != null)
            {
                var latestSettings = session.Database.GetAccount(sid)?.Firewall
                                     ?? new FirewallAccountSettings();
                using var blockedDlg = new BlockedConnectionsDialog(
                    displayName: username,
                    reader: blockedConnectionReader,
                    dnsResolver: dnsResolver,
                    existingAllowlist: latestSettings.Allowlist.AsReadOnly(),
                    enableAuditLogging: true);
                if (blockedDlg.ShowDialog(owner) == DialogResult.OK && blockedDlg.SelectedEntries.Count > 0)
                {
                    var settings = session.Database.GetAccount(sid)?.Firewall
                                   ?? new FirewallAccountSettings();
                    settings.Allowlist.AddRange(blockedDlg.SelectedEntries);
                    FirewallAccountSettings.UpdateOrRemove(session.Database, sid, settings);
                    sessionSaver.SaveAndRefresh();
                }
            }
        };
    }
}