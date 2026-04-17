using RunFence.Account.UI;
using RunFence.Core.Models;
using RunFence.Firewall.UI;
using RunFence.Firewall.UI.Forms;
using RunFence.Launch;
using RunFence.Persistence;

namespace RunFence.Wizard.Templates;

/// <summary>
/// Handles firewall operations for the AI agent wizard template:
/// applying restrictive rules after package installation and building the
/// post-wizard action that opens the allowlist and blocked-connections dialogs.
/// </summary>
public class AiAgentFirewallOrchestrator(
    FirewallApplyHelper firewallApplyHelper,
    FirewallDialogFactory dialogFactory,
    IDatabaseProvider databaseProvider,
    ILaunchFacade launchFacade,
    AccountToolResolver accountToolResolver)
{
    /// <summary>
    /// Updates firewall settings in the database and applies rules via <see cref="FirewallApplyHelper"/>.
    /// Reports progress and non-fatal errors via <paramref name="progress"/>.
    /// </summary>
    public async Task ApplyRestrictiveRulesAsync(
        string sid,
        string username,
        FirewallAccountSettings settings,
        IWizardProgressReporter progress)
    {
        var database = databaseProvider.GetDatabase();
        var previousSettings = (database.GetAccount(sid)?.Firewall ?? new FirewallAccountSettings()).Clone();
        FirewallAccountSettings.UpdateOrRemove(database, sid, settings);
        progress.ReportStatus("Applying firewall rules...");
        await firewallApplyHelper.ApplyWithRollbackAsync(
            sid: sid,
            username: username,
            previous: previousSettings,
            final: settings,
            database: database,
            saveAction: () => { },
            reportError: progress.ReportError);
    }

    /// <summary>
    /// Builds the post-wizard action that:
    /// <list type="number">
    ///   <item>Launches the AI tool (<paramref name="toolPath"/>) or a terminal when no tool path is set.</item>
    ///   <item>Opens the firewall allowlist dialog so the user can whitelist required domains.</item>
    ///   <item>Automatically opens the blocked-connections dialog (with audit logging enabled) when the
    ///         allowlist dialog first appears, so the user can see real traffic without closing the allowlist.</item>
    /// </list>
    /// Returns <c>null</c> when firewall network info is unavailable (firewall not configured).
    /// </summary>
    public Action<IWin32Window>? BuildPostWizardAction(
        string sid,
        string username,
        SessionContext session,
        IWizardSessionSaver sessionSaver,
        string? toolPath)
    {
        if (!dialogFactory.IsAvailable)
            return null;

        return owner =>
        {
            // Launch the tool or terminal first so the agent can start while the user configures firewall.
            try
            {
                if (!string.IsNullOrEmpty(toolPath))
                {
                    launchFacade.LaunchFile(new ProcessLaunchTarget(toolPath), new AccountLaunchIdentity(sid), permissionPrompt: (_, _) => true);
                }
                else
                {
                    var terminalExe = accountToolResolver.ResolveTerminalExe(sid);
                    var profilePath = accountToolResolver.GetProfileRoot(sid);
                    launchFacade.LaunchFile(new ProcessLaunchTarget(terminalExe, WorkingDirectory: profilePath), new AccountLaunchIdentity(sid), permissionPrompt: (_, _) => true);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                MessageBox.Show(owner, $"Failed to launch: {ex.Message}", "RunFence",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            var currentSettings = session.Database.GetAccount(sid)?.Firewall
                                  ?? new FirewallAccountSettings();

            using var allowlistDlg = dialogFactory.CreateAllowlistDialog(
                current: currentSettings.Allowlist.ToList(),
                displayName: username,
                allowInternet: currentSettings.AllowInternet,
                allowLan: currentSettings.AllowLan,
                allowLocalhost: currentSettings.AllowLocalhost,
                allowedLocalhostPorts: currentSettings.LocalhostPortExemptions,
                filterEphemeralLoopback: currentSettings.FilterEphemeralLoopback);

            if (allowlistDlg != null)
            {
                allowlistDlg.Applied += (_, args) =>
                {
                    var existing = session.Database.GetAccount(sid)?.Firewall
                                   ?? new FirewallAccountSettings();
                    var previousSettings = existing.Clone();
                    existing.AllowInternet = allowlistDlg.AllowInternet;
                    existing.AllowLan = allowlistDlg.AllowLan;
                    existing.AllowLocalhost = allowlistDlg.AllowLocalhost;
                    existing.LocalhostPortExemptions = allowlistDlg.AllowedLocalhostPorts.ToList();
                    existing.FilterEphemeralLoopback = allowlistDlg.FilterEphemeralLoopback;
                    existing.Allowlist = allowlistDlg.Result;
                    FirewallAccountSettings.UpdateOrRemove(session.Database, sid, existing);
                    sessionSaver.SaveAndRefresh();
                    var finalSettings = session.Database.GetAccount(sid)?.Firewall ?? new FirewallAccountSettings();
                    bool rolledBack = firewallApplyHelper.ApplyWithRollback(
                        owner: owner,
                        sid: sid,
                        username: username,
                        previous: previousSettings,
                        final: finalSettings,
                        database: session.Database,
                        saveAction: sessionSaver.SaveAndRefresh);
                    if (rolledBack)
                        args.RolledBack = true;
                };
                // Open blocked-connections dialog automatically when the allowlist dialog appears,
                // so the user can see what the agent is trying to reach and add allowlist entries
                // without closing the allowlist first. Audit logging is enabled inside that dialog.
                allowlistDlg.AutoOpenBlockedConnectionsOnShow();
                allowlistDlg.ShowDialog(owner);
            }
        };
    }
}
