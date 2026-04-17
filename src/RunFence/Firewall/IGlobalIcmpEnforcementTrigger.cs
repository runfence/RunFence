using RunFence.Core.Models;

namespace RunFence.Firewall;

/// <summary>
/// Triggers enforcement of the global ICMP block rules, synchronously or asynchronously.
/// </summary>
public interface IGlobalIcmpEnforcementTrigger
{
    void EnforceGlobalIcmpBlock(AppDatabase database);
    Task EnforceGlobalIcmpBlockAsync(GlobalIcmpPolicyInput input, CancellationToken cancellationToken);
}
