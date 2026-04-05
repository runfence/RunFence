using RunFence.Core.Models;

namespace RunFence.Firewall;

/// <summary>
/// Implements DNS resolution and DNS server address lookup for firewall rule computation.
/// Extracted from <see cref="FirewallService"/> to separate network info concerns.
/// </summary>
public class FirewallNetworkInfoService(IDnsResolver dnsResolver, INetworkInterfaceInfoProvider networkInfo) : IFirewallNetworkInfo
{
    public IReadOnlyList<string> GetDnsServerAddresses() => networkInfo.GetDnsServerAddresses();

    public async Task<Dictionary<string, List<string>>> ResolveDomainEntriesAsync(
        IReadOnlyList<FirewallAllowlistEntry> entries)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var domainEntries = entries.Where(e => e.IsDomain).ToList();
        var tasks = domainEntries
            .Select(async e => (e.Value, await dnsResolver.ResolveAsync(e.Value)))
            .ToList();
        foreach (var (domain, ips) in await Task.WhenAll(tasks))
            result[domain] = ips.ToList();
        return result;
    }
}