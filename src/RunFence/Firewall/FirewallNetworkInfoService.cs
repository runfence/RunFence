using RunFence.Core.Models;

namespace RunFence.Firewall;

/// <summary>
/// Implements DNS resolution and DNS server address lookup for firewall rule computation.
/// Provides firewall network information without coupling callers to rule enforcement.
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
        {
            var resolved = ips.Where(ip => !string.IsNullOrWhiteSpace(ip)).ToList();
            if (resolved.Count > 0)
                result[domain] = resolved;
        }

        return result;
    }
}
