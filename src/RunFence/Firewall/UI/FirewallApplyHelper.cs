using RunFence.Core;
using RunFence.Core.Models;
using System.Linq;

namespace RunFence.Firewall.UI;

/// <summary>
/// Encapsulates the try/catch pattern for applying firewall settings with rollback on failure.
/// On <see cref="FirewallApplyPhase.AccountRules"/> failure: restores DB to previous settings,
/// persists the rollback only when the failed apply had already been saved, and reports/shows the error.
/// Global ICMP enforcement failures are returned as warnings in
/// <see cref="FirewallApplyResult.EnforcementEntries"/> (no rollback).
/// </summary>
public class FirewallApplyHelper(
    IAccountFirewallSettingsApplier firewallSettingsApplier,
    DynamicPortRangeChecker portRangeChecker,
    ILoggingService log) : IFirewallApplyHelper
{
    /// <summary>
    /// Applies firewall settings synchronously. On <see cref="FirewallApplyPhase.AccountRules"/> failure,
    /// rolls back the database to <paramref name="previous"/>, persists the rollback only when needed,
    /// and shows an error dialog.
    /// </summary>
    /// <returns><c>true</c> if a rollback occurred (caller should update UI accordingly).</returns>
    public bool ApplyWithRollback(
        IWin32Window? owner,
        string sid,
        string username,
        FirewallAccountSettings? previous,
        FirewallAccountSettings final,
        AppDatabase database,
        Action saveAction)
    {
        try
        {
            var result = firewallSettingsApplier.ApplyAccountFirewallSettings(sid, username, previous, final, database, saveAction);
            if (result.HasBlockingFailure)
            {
                var blockingFailure = result.FirstBlockingFailure!;
                log.Error($"Failed to apply firewall rules for {username}", new InvalidOperationException(blockingFailure.Error));
                FirewallAccountSettings.UpdateOrRemove(database, sid, previous?.Clone() ?? new FirewallAccountSettings());
                var rollbackError = result.ConfigSaved ? TrySave(saveAction, sid) : null;
                var applyException = new FirewallApplyException(
                    FirewallApplyPhase.AccountRules,
                    sid,
                    new InvalidOperationException(blockingFailure.Error ?? "Firewall enforcement failed."));
                FirewallApplyErrorHandler.ShowApplyFailure(owner, applyException, rollbackError);
                return true;
            }

            var warningEntries = (result.EnforcementEntries ?? [])
                .Where(entry => entry.Status == FirewallEnforcementStatus.RetryScheduled)
                .ToList();
            if (warningEntries.Count > 0)
            {
                MessageBox.Show(owner,
                    BuildEnforcementWarningMessage(result, warningEntries),
                    "RunFence",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            _ = portRangeChecker.CheckIfNeededAsync(final);
            return false;
        }
        catch (FirewallApplyException ex) when (ex.Phase == FirewallApplyPhase.AccountRules)
        {
            log.Error($"Failed to apply firewall rules for {username}", ex);
            FirewallAccountSettings.UpdateOrRemove(database, sid, previous?.Clone() ?? new FirewallAccountSettings());
            var rollbackError = TrySave(saveAction, sid);
            FirewallApplyErrorHandler.ShowApplyFailure(owner, ex, rollbackError);
            return true;
        }
    }

    /// <summary>
    /// Applies firewall settings asynchronously. On <see cref="FirewallApplyPhase.AccountRules"/> failure,
    /// rolls back the database to <paramref name="previous"/>, persists the rollback only when needed,
    /// and reports errors via <paramref name="reportError"/>.
    /// On <see cref="FirewallApplyPhase.GlobalIcmp"/> failure, reports a warning via <paramref name="reportError"/>.
    /// </summary>
    /// <returns><c>true</c> if a rollback occurred.</returns>
    public async Task<bool> ApplyWithRollbackAsync(
        string sid,
        string username,
        FirewallAccountSettings? previous,
        FirewallAccountSettings final,
        AppDatabase database,
        Action saveAction,
        Action<string> reportError)
    {
        try
        {
            var result = await firewallSettingsApplier.ApplyAccountFirewallSettingsAsync(sid, username, previous, final, database, saveAction);
            if (result == null)
                return false;
            if (result.HasBlockingFailure)
            {
                var blockingFailure = result.FirstBlockingFailure!;
                log.Error($"Failed to apply firewall rules for {username}", new InvalidOperationException(blockingFailure.Error));
                FirewallAccountSettings.UpdateOrRemove(database, sid, previous?.Clone() ?? new FirewallAccountSettings());
                var rollbackError = result.ConfigSaved ? TrySave(saveAction, sid) : null;
                reportError($"Firewall rules: {blockingFailure.Error}");
                if (rollbackError != null)
                    reportError($"Firewall rollback save: {rollbackError.Message}");
                return true;
            }

            var warningEntries = (result.EnforcementEntries ?? [])
                .Where(entry => entry.Status == FirewallEnforcementStatus.RetryScheduled)
                .ToList();
            if (warningEntries.Count > 0)
                reportError(BuildEnforcementWarningMessage(result, warningEntries));
            await portRangeChecker.CheckIfNeededAsync(final);
            return false;
        }
        catch (FirewallApplyException ex) when (ex.Phase == FirewallApplyPhase.GlobalIcmp)
        {
            reportError($"Global ICMP firewall rule: {ex.CauseMessage}");
            return false;
        }
        catch (FirewallApplyException ex) when (ex.Phase == FirewallApplyPhase.AccountRules)
        {
            log.Error($"Failed to apply firewall rules for {username}", ex);
            FirewallAccountSettings.UpdateOrRemove(database, sid, previous?.Clone() ?? new FirewallAccountSettings());
            var rollbackError = TrySave(saveAction, sid);
            reportError($"Firewall rules: {ex.CauseMessage}");
            if (rollbackError != null)
                reportError($"Firewall rollback save: {rollbackError.Message}");
            return true;
        }
    }

    private Exception? TrySave(Action saveAction, string sid)
    {
        try
        {
            saveAction();
            return null;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to save firewall settings rollback for SID '{sid}'", ex);
            return ex;
        }
    }

    private static string BuildEnforcementWarningMessage(
        FirewallApplyResult result,
        IReadOnlyList<FirewallEnforcementEntry> warningEntries)
    {
        var warningText = string.Join(
            Environment.NewLine,
            warningEntries.Select(entry =>
                $"{entry.Layer}: {entry.Error ?? "enforcement retry scheduled"}"));
        return result.ConfigSaved
            ? $"Firewall settings were saved, but some enforcement actions will retry:{Environment.NewLine}{warningText}"
            : $"Firewall settings were not saved because some enforcement actions did not complete:{Environment.NewLine}{warningText}";
    }
}
