using RunFence.Core.Models;

namespace RunFence.Firewall;

public interface IFirewallEnforcementOrchestrator
{
    void EnforceAll(AppDatabase database);
}
