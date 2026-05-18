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
    /// <returns>The user-visible message, if any.</returns>
    SetAllowInternetResult SetAllowInternet(string sid, bool allow, FirewallAccountSettings? existing);
}

public record SetAllowInternetResult(string? Message);
