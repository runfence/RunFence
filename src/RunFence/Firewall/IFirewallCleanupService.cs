using RunFence.Core.Models;

namespace RunFence.Firewall;

public interface IFirewallCleanupService
{
    void RemoveAllRules(string sid);

    void RemoveAll(AppDatabase database);

    void CleanupOrphanedRules(IReadOnlySet<string> activeSids, IEnumerable<string> allKnownSids);
}
