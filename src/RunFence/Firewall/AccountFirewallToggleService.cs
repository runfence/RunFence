using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Firewall;

/// <summary>
/// Applies an AllowInternet toggle for a single account by cloning its current firewall settings,
/// setting the new value, persisting it via <see cref="FirewallAccountSettings.UpdateOrRemove"/>,
/// and enforcing the rules via <see cref="IAccountFirewallSettingsApplier"/>.
/// On <see cref="FirewallApplyPhase.AccountRules"/> failure, reverts the DB change and returns
/// an error message. On <see cref="FirewallApplyPhase.GlobalIcmp"/> failure, returns a warning
/// without rollback (rules were applied).
/// </summary>
public class AccountFirewallToggleService(
    IFirewallSettingsService firewallSettingsService,
    IAccountFirewallSettingsApplier firewallSettingsApplier,
    ILoggingService log) : IAccountFirewallToggle
{
    public string? SetAllowInternet(string sid, bool allow, FirewallAccountSettings? existing)
    {
        var settings = existing?.Clone() ?? new FirewallAccountSettings();
        var previousSettings = settings.Clone();
        settings.AllowInternet = allow;

        var (database, resolvedUsername) = firewallSettingsService.GetDatabaseAndUsername(sid);
        FirewallAccountSettings.UpdateOrRemove(database, sid, settings);

        var finalSettings = database.GetAccount(sid)?.Firewall ?? new FirewallAccountSettings();
        try
        {
            firewallSettingsApplier.ApplyAccountFirewallSettings(
                sid,
                resolvedUsername,
                previousSettings,
                finalSettings,
                database);
            return null;
        }
        catch (FirewallApplyException ex) when (ex.Phase == FirewallApplyPhase.AccountRules)
        {
            log.Error($"Failed to apply firewall rules for {resolvedUsername}", ex);
            FirewallAccountSettings.UpdateOrRemove(database, sid, previousSettings);
            return ex.CauseMessage;
        }
        catch (FirewallApplyException ex) when (ex.Phase == FirewallApplyPhase.GlobalIcmp)
        {
            log.Error($"Failed to enforce global ICMP after applying firewall rules for {resolvedUsername}", ex);
            return ex.CauseMessage;
        }
    }
}
