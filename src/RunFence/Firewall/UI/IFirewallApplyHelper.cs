using RunFence.Core.Models;

namespace RunFence.Firewall.UI;

/// <summary>
/// Testable contract for applying firewall settings with rollback semantics.
/// </summary>
public interface IFirewallApplyHelper
{
    bool ApplyWithRollback(
        IWin32Window? owner,
        string sid,
        string username,
        FirewallAccountSettings? previous,
        FirewallAccountSettings final,
        AppDatabase database,
        Action saveAction);

    Task<bool> ApplyWithRollbackAsync(
        string sid,
        string username,
        FirewallAccountSettings? previous,
        FirewallAccountSettings final,
        AppDatabase database,
        Action saveAction,
        Action<string> reportError);
}
