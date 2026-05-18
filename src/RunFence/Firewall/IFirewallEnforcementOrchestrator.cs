using RunFence.Core.Models;

namespace RunFence.Firewall;

public interface IFirewallEnforcementOrchestrator
{
    EnforceAllResult EnforceAll(AppDatabase database);
}
