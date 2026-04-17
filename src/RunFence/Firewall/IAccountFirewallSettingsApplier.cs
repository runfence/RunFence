using RunFence.Core.Models;

namespace RunFence.Firewall;

public interface IAccountFirewallSettingsApplier
{
    Task<FirewallApplyResult> ApplyAccountFirewallSettingsAsync(
        string sid,
        string username,
        FirewallAccountSettings? previousSettings,
        FirewallAccountSettings settings,
        AppDatabase database,
        CancellationToken cancellationToken = default);

    FirewallApplyResult ApplyAccountFirewallSettings(
        string sid,
        string username,
        FirewallAccountSettings? previousSettings,
        FirewallAccountSettings settings,
        AppDatabase database);
}
