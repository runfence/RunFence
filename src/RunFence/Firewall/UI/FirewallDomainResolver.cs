using RunFence.Core.Models;

namespace RunFence.Firewall.UI;

/// <summary>
/// Resolves domain names in firewall allowlist entries to their IP addresses.
/// Wraps <see cref="IFirewallNetworkInfo"/> for batch resolution and <see cref="IDnsResolver"/>
/// for single-entry resolution, providing a focused UI-layer interface for the dialog.
/// </summary>
public class FirewallDomainResolver(IFirewallNetworkInfo firewallNetworkInfo, IDnsResolver dnsResolver)
{
    /// <summary>
    /// Resolves all domain entries in the list concurrently.
    /// Returns a dictionary mapping domain name to resolved IP address strings.
    /// </summary>
    public Task<Dictionary<string, List<string>>> ResolveAllAsync(
        IReadOnlyList<FirewallAllowlistEntry> entries)
        => firewallNetworkInfo.ResolveDomainEntriesAsync(entries);

    /// <summary>
    /// Resolves a single domain entry to its IP address strings.
    /// Returns an empty list if the entry is not a domain, resolution fails, or produces no results.
    /// </summary>
    public async Task<IReadOnlyList<string>> ResolveEntryAsync(FirewallAllowlistEntry entry)
    {
        if (!entry.IsDomain)
            return [];
        return await dnsResolver.ResolveAsync(entry.Value);
    }
}
