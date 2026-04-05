using RunFence.Core.Models;

namespace RunFence.Firewall;

public interface IFirewallNetworkInfo
{
    IReadOnlyList<string> GetDnsServerAddresses();
    Task<Dictionary<string, List<string>>> ResolveDomainEntriesAsync(IReadOnlyList<FirewallAllowlistEntry> entries);
}