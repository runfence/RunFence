using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Firewall;

/// <summary>
/// Handles DNS resolution of domain allowlist entries in bulk or per-domain.
/// Encapsulates the resolution strategy (bulk with fallback to per-domain) and timeout handling.
/// </summary>
public class FirewallDomainBatchResolver(IFirewallNetworkInfo firewallNetworkInfo, ILoggingService log)
{
    /// <summary>
    /// Resolves domain allowlist entries in bulk with a 30-second timeout.
    /// Falls back to per-domain resolution on timeout or failure.
    /// </summary>
    /// <returns>Dictionary mapping domain names to resolved IP address lists.</returns>
    public Dictionary<string, List<string>> ResolveBatch(IReadOnlyList<FirewallAllowlistEntry> domainEntries)
    {
        if (domainEntries.Count == 0)
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, List<string>> resolved;
        try
        {
            resolved = firewallNetworkInfo.ResolveDomainEntriesAsync(domainEntries)
                .WaitAsync(TimeSpan.FromSeconds(30))
                .GetAwaiter()
                .GetResult();
        }
        catch (TimeoutException)
        {
            log.Warn("FirewallDomainBatchResolver: bulk DNS resolution timed out after 30 seconds, falling back to per-domain resolution.");
            return ResolvePerDomain(domainEntries);
        }
        catch (Exception ex)
        {
            log.Warn($"FirewallDomainBatchResolver: bulk DNS resolution failed: {ex.Message}");
            return ResolvePerDomain(domainEntries);
        }

        var freshResolved = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in domainEntries)
        {
            if (!resolved.TryGetValue(entry.Value, out var addresses)
                || addresses.All(address => string.IsNullOrWhiteSpace(address)))
            {
                log.Warn($"FirewallDomainBatchResolver: DNS returned no addresses for {entry.Value}");
                continue;
            }

            freshResolved[entry.Value] = addresses.ToList();
        }

        return freshResolved;
    }

    private Dictionary<string, List<string>> ResolvePerDomain(IReadOnlyList<FirewallAllowlistEntry> domainEntries)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in domainEntries)
        {
            try
            {
                var single = firewallNetworkInfo.ResolveDomainEntriesAsync([entry])
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .GetAwaiter().GetResult();
                if (single.TryGetValue(entry.Value, out var addrs)
                    && addrs.Any(a => !string.IsNullOrWhiteSpace(a)))
                    result[entry.Value] = addrs.ToList();
            }
            catch (TimeoutException)
            {
                log.Warn($"FirewallDomainBatchResolver: DNS resolution timed out after 10 seconds for {entry.Value}");
            }
            catch (Exception perEx)
            {
                log.Warn($"FirewallDomainBatchResolver: DNS resolution failed for {entry.Value}: {perEx.Message}");
            }
        }
        return result;
    }

    /// <summary>
    /// Returns the current DNS server addresses from the network interface.
    /// </summary>
    public IReadOnlyList<string> GetDnsServerAddresses() => firewallNetworkInfo.GetDnsServerAddresses();
}
