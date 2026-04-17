using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Firewall.UI;

/// <summary>
/// Encapsulates the try/catch pattern for applying firewall settings with rollback on failure.
/// On <see cref="FirewallApplyPhase.AccountRules"/> failure: restores DB to previous settings,
/// calls <c>saveAction</c>, and reports/shows the error.
/// On <see cref="FirewallApplyPhase.GlobalIcmp"/> failure: reports a warning only
/// (rules were applied; only global ICMP enforcement failed; no rollback).
/// </summary>
public class FirewallApplyHelper(IAccountFirewallSettingsApplier firewallSettingsApplier, ILoggingService log)
{
    /// <summary>
    /// Applies firewall settings synchronously. On <see cref="FirewallApplyPhase.AccountRules"/> failure,
    /// rolls back the database to <paramref name="previous"/>, calls <paramref name="saveAction"/>,
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
            firewallSettingsApplier.ApplyAccountFirewallSettings(sid, username, previous, final, database);
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
        catch (FirewallApplyException ex) when (ex.Phase == FirewallApplyPhase.GlobalIcmp)
        {
            log.Error($"Failed to enforce global ICMP after applying firewall rules for {username}", ex);
            MessageBox.Show(owner,
                $"Firewall rules were saved and applied, but global ICMP enforcement failed: {ex.CauseMessage}",
                "RunFence",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }
    }

    /// <summary>
    /// Applies firewall settings asynchronously. On <see cref="FirewallApplyPhase.AccountRules"/> failure,
    /// rolls back the database to <paramref name="previous"/>, calls <paramref name="saveAction"/>,
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
            await firewallSettingsApplier.ApplyAccountFirewallSettingsAsync(sid, username, previous, final, database);
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
        catch (FirewallApplyException ex) when (ex.Phase == FirewallApplyPhase.GlobalIcmp)
        {
            log.Error($"Failed to enforce global ICMP after applying firewall rules for {username}", ex);
            reportError($"Global ICMP firewall rule: {ex.CauseMessage}");
            return false;
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
}
