using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using System.Linq;

namespace RunFence.Firewall;

/// <summary>
/// Applies an AllowInternet toggle for a single account by cloning its current firewall settings,
/// setting the new value, and enforcing the rules via
/// <see cref="IAccountFirewallSettingsApplier"/>.
/// Blocking account-rule/WFP failures revert the in-memory settings and re-save when a blocking
/// failure happened after persistence. Data-change notification is raised here after persisted
/// or rollback state changes so callers do not need UI refresh coupling. Global ICMP enforcement
/// failures are returned as warnings in <see cref="FirewallApplyResult.EnforcementEntries"/>
/// and do not rollback the saved setting.
/// </summary>
public class AccountFirewallToggleService(
    IFirewallSettingsService firewallSettingsService,
    IAccountFirewallSettingsApplier firewallSettingsApplier,
    ISessionSaver sessionSaver,
    IDataChangeNotifier dataChangeNotifier,
    ILoggingService log) : IAccountFirewallToggle
{
    public SetAllowInternetResult SetAllowInternet(string sid, bool allow, FirewallAccountSettings? existing)
    {
        var settings = existing?.Clone() ?? new FirewallAccountSettings();
        var previousSettings = settings.Clone();
        settings.AllowInternet = allow;

        var (database, resolvedUsername) = firewallSettingsService.GetDatabaseAndUsername(sid);
        var applyResult = firewallSettingsApplier.ApplyAccountFirewallSettings(
            sid,
            resolvedUsername,
            previousSettings,
            settings,
            database,
            sessionSaver.SaveConfig);
        if (applyResult.HasBlockingFailure)
        {
            var blockingFailure = applyResult.FirstBlockingFailure!;
            string? rollbackSaveError = null;
            FirewallAccountSettings.UpdateOrRemove(database, sid, previousSettings);
            if (applyResult.ConfigSaved)
            {
                try
                {
                    sessionSaver.SaveConfig();
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to save firewall rollback for {resolvedUsername}", ex);
                    rollbackSaveError = ex.Message;
                }
            }

            log.Error(
                $"Failed to apply firewall rules for {resolvedUsername}",
                new InvalidOperationException(blockingFailure.Error));
            if (rollbackSaveError != null)
            {
                dataChangeNotifier.NotifyDataChanged();
                return new SetAllowInternetResult(
                    $"{blockingFailure.Error}{Environment.NewLine}Firewall rollback save: {rollbackSaveError}");
            }

            dataChangeNotifier.NotifyDataChanged();
            return new SetAllowInternetResult(blockingFailure.Error);
        }

        var warningEntries = applyResult.EnforcementEntries
            .Where(entry => entry.Status == FirewallEnforcementStatus.RetryScheduled)
            .ToList();
        if (warningEntries.Count > 0)
        {
            dataChangeNotifier.NotifyDataChanged();
            return new SetAllowInternetResult(
                string.Join(
                    Environment.NewLine,
                    warningEntries.Select(entry =>
                        $"{entry.Layer}: {entry.Error ?? "enforcement retry scheduled"}")));
        }

        NotifyIfConfigSaved(applyResult);
        return new SetAllowInternetResult(null);
    }

    private void NotifyIfConfigSaved(FirewallApplyResult applyResult)
    {
        if (applyResult.ConfigSaved)
            dataChangeNotifier.NotifyDataChanged();
    }
}
