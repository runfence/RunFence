using RunFence.Core.Models;

namespace RunFence.Firewall;

public interface IAccountFirewallToggle
{
    /// <summary>
    /// Toggles the AllowInternet setting for the given account. Clones the existing settings,
    /// applies the change, and enforces the firewall rules. On <see cref="FirewallApplyPhase.AccountRules"/>
    /// failure, reverts the DB settings and returns an error message. On <see cref="FirewallApplyPhase.GlobalIcmp"/>
    /// failure, returns a warning message without rolling back.
    /// </summary>
    /// <returns>An error/warning message, or null on success.</returns>
    string? SetAllowInternet(string sid, bool allow, FirewallAccountSettings? existing);
}
