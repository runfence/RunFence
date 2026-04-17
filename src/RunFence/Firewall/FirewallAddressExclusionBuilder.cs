using System.Net;
using System.Net.Sockets;
using RunFence.Core.Models;

namespace RunFence.Firewall;

public class FirewallAddressExclusionBuilder(
    FirewallAddressRangeBuilder rangeBuilder,
    INetworkInterfaceInfoProvider networkInfo)
{
    public FirewallAddressComputationResult ComputeInternetAddresses(
        string sid,
        FirewallAccountSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache)
    {
        var allowlist = ComputeAllowlistExclusions(sid, settings, resolvedDomainsCache);
        return new FirewallAddressComputationResult(
            rangeBuilder.BuildInternetIPv4Range(allowlist.Exclusions),
            rangeBuilder.BuildInternetIPv6Range(allowlist.Exclusions),
            allowlist.PendingDomains);
    }

    public FirewallAddressComputationResult ComputeLanAddresses(
        string sid,
        FirewallAccountSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache)
    {
        if (settings.Allowlist.Count == 0 && !settings.AllowLocalhost)
        {
            return new FirewallAddressComputationResult(
                rangeBuilder.BuildLanIPv4Range(),
                rangeBuilder.BuildLanIPv6Range(),
                []);
        }

        var allowlist = ComputeAllowlistExclusions(sid, settings, resolvedDomainsCache);
        return new FirewallAddressComputationResult(
            rangeBuilder.BuildLanIPv4Range(allowlist.Exclusions),
            rangeBuilder.BuildLanIPv6Range(allowlist.Exclusions),
            allowlist.PendingDomains);
    }

    public FirewallAddressPair ComputeLocalAddressRanges()
    {
        var localAddresses = networkInfo.GetLocalAddresses();
        var ipv4 = string.Join(",", localAddresses
            .Where(a => IPAddress.TryParse(a, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork));
        var ipv6 = string.Join(",", localAddresses
            .Where(a => IPAddress.TryParse(a, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6));
        return new FirewallAddressPair(ipv4, ipv6);
    }

    public FirewallAllowlistExclusionResult ComputeAllowlistExclusions(
        string sid,
        FirewallAccountSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache)
        => ComputeAllowlistExclusions(
            sid,
            settings,
            resolvedDomainsCache,
            includeLocalAddresses: true);

    private FirewallAllowlistExclusionResult ComputeAllowlistExclusions(
        string sid,
        FirewallAccountSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache,
        bool includeLocalAddresses)
    {
        var allExclusions = new List<string>();
        var pendingDomains = new List<FirewallPendingDomainResolution>();
        var pendingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool hasAllowlistEntries = settings.Allowlist.Count > 0;
        if (hasAllowlistEntries)
            allExclusions.AddRange(networkInfo.GetDnsServerAddresses());

        if (includeLocalAddresses && settings.AllowLocalhost)
            allExclusions.AddRange(networkInfo.GetLocalAddresses());

        foreach (var entry in settings.Allowlist)
        {
            if (!entry.IsDomain)
            {
                allExclusions.Add(entry.Value);
                continue;
            }

            if (resolvedDomainsCache.TryGetValue(entry.Value, out var preResolved))
            {
                allExclusions.AddRange(preResolved);
                continue;
            }

            var pendingKey = $"{sid}\0{entry.Value}";
            if (pendingKeys.Add(pendingKey))
                pendingDomains.Add(new FirewallPendingDomainResolution(sid, entry.Value));
        }

        return new FirewallAllowlistExclusionResult(allExclusions, pendingDomains);
    }

    public FirewallCommonIcmpExclusionResult ComputeCommonIcmpExclusions(
        IReadOnlyList<AccountEntry> blockedAccounts,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolvedDomainsCache)
    {
        if (blockedAccounts.Count == 0)
            return new FirewallCommonIcmpExclusionResult([], []);

        HashSet<string>? intersection = null;
        var pendingDomains = new List<FirewallPendingDomainResolution>();
        var pendingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var account in blockedAccounts)
        {
            var exclusions = ComputeAllowlistExclusions(
                account.Sid,
                account.Firewall,
                resolvedDomainsCache,
                includeLocalAddresses: false);
            foreach (var pendingDomain in exclusions.PendingDomains)
            {
                var pendingKey = $"{pendingDomain.Sid}\0{pendingDomain.Domain}";
                if (pendingKeys.Add(pendingKey))
                    pendingDomains.Add(pendingDomain);
            }

            var exclusionSet = new HashSet<string>(exclusions.Exclusions, StringComparer.OrdinalIgnoreCase);
            if (intersection == null)
                intersection = exclusionSet;
            else
                intersection.IntersectWith(exclusionSet);
        }

        return new FirewallCommonIcmpExclusionResult([.. intersection ?? []], pendingDomains);
    }

}
