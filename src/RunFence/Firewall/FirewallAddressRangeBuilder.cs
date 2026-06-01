using System.Net;
using System.Net.Sockets;

namespace RunFence.Firewall;

/// <summary>
/// Computes Windows Firewall RemoteAddress range strings by performing CIDR subtraction.
/// Non-static for testability.
/// </summary>
public class FirewallAddressRangeBuilder
{
    private const string LanIpv4RangeText = "10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,169.254.0.0/16,100.64.0.0/10";
    private const string LanIpv6RangeText = "fe80::/10,fc00::/7";

    // IPv4 base: everything except loopback and LAN
    private static readonly IReadOnlyList<(string Network, int Prefix)> IPv4BaseRanges =
    [
        ("0.0.0.0", 0)
    ];

    private static readonly IReadOnlyList<(string Network, int Prefix)> IPv4BaseExclusions =
    [
        ("127.0.0.0", 8),
        ("10.0.0.0", 8),
        ("172.16.0.0", 12),
        ("192.168.0.0", 16),
        ("169.254.0.0", 16), // link-local (APIPA)
        ("100.64.0.0", 10)   // CGNAT / RFC 6598 (Tailscale, WireGuard)
    ];

    // IPv6 base: everything except loopback and LAN
    private static readonly IReadOnlyList<(string Network, int Prefix)> IPv6BaseRanges =
    [
        ("::", 0)
    ];

    private static readonly IReadOnlyList<(string Network, int Prefix)> IPv6BaseExclusions =
    [
        ("::1", 128),
        ("fe80::", 10),
        ("fc00::", 7)
    ];

    // LAN base ranges
    private static readonly IReadOnlyList<(string Network, int Prefix)> IPv4LanBaseRanges =
    [
        ("10.0.0.0", 8),
        ("172.16.0.0", 12),
        ("192.168.0.0", 16),
        ("169.254.0.0", 16), // link-local (APIPA)
        ("100.64.0.0", 10)   // CGNAT / RFC 6598 (Tailscale, WireGuard)
    ];

    private static readonly IReadOnlyList<(string Network, int Prefix)> IPv6LanBaseRanges =
    [
        ("fe80::", 10),
        ("fc00::", 7)
    ];

    /// <summary>
    /// Returns comma-separated IPv4 internet block range with given IPs excluded.
    /// </summary>
    public string BuildInternetIPv4Range(IReadOnlyList<string> exclusions)
    {
        var allExclusions = IPv4BaseExclusions.Concat(ParseIPv4Exclusions(exclusions)).ToList();
        var ranges = ApplyExclusions(IPv4BaseRanges, allExclusions, AddressFamily.InterNetwork);
        return FormatRanges(ranges);
    }

    /// <summary>
    /// Returns comma-separated IPv6 internet block range with given IPs excluded.
    /// </summary>
    public string BuildInternetIPv6Range(IReadOnlyList<string> exclusions)
    {
        var allExclusions = IPv6BaseExclusions.Concat(ParseIPv6Exclusions(exclusions)).ToList();
        var ranges = ApplyExclusions(IPv6BaseRanges, allExclusions, AddressFamily.InterNetworkV6);
        return FormatRanges(ranges);
    }

    /// <summary>Returns fixed IPv4 LAN ranges.</summary>
    public string BuildLanIPv4Range() => LanIpv4RangeText;

    /// <summary>Returns fixed IPv6 LAN ranges.</summary>
    public string BuildLanIPv6Range() => LanIpv6RangeText;

    /// <summary>
    /// Returns comma-separated IPv4 LAN block range with given IPs excluded.
    /// </summary>
    public string BuildLanIPv4Range(IReadOnlyList<string> exclusions)
    {
        var ranges = ApplyExclusions(IPv4LanBaseRanges,
            ParseIPv4Exclusions(exclusions).ToList(), AddressFamily.InterNetwork);
        return FormatRanges(ranges);
    }

    /// <summary>
    /// Returns comma-separated IPv6 LAN block range with given IPs excluded.
    /// </summary>
    public string BuildLanIPv6Range(IReadOnlyList<string> exclusions)
    {
        var ranges = ApplyExclusions(IPv6LanBaseRanges,
            ParseIPv6Exclusions(exclusions).ToList(), AddressFamily.InterNetworkV6);
        return FormatRanges(ranges);
    }

    public static bool IsValidIpOrCidr(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var slashIdx = value.IndexOf('/');
        if (slashIdx < 0)
            return IPAddress.TryParse(value, out _);

        var ipPart = value[..slashIdx];
        var prefixPart = value[(slashIdx + 1)..];
        if (!IPAddress.TryParse(ipPart, out var addr))
            return false;
        if (!int.TryParse(prefixPart, out var prefix))
            return false;
        var maxPrefix = addr.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        return prefix >= 0 && prefix <= maxPrefix;
    }

    private static IEnumerable<(string Network, int Prefix)> ParseIPv4Exclusions(IReadOnlyList<string> exclusions)
        => ParseExclusions(exclusions, AddressFamily.InterNetwork, maxPrefix: 32);

    private static IEnumerable<(string Network, int Prefix)> ParseIPv6Exclusions(IReadOnlyList<string> exclusions)
        => ParseExclusions(exclusions, AddressFamily.InterNetworkV6, maxPrefix: 128);

    private static IEnumerable<(string Network, int Prefix)> ParseExclusions(
        IReadOnlyList<string> exclusions,
        AddressFamily family,
        int maxPrefix)
    {
        foreach (var exc in exclusions)
        {
            if (CidrRangeHelper.TryParseCidr(exc, out var net, out var prefix) && net.AddressFamily == family)
                yield return (net.ToString(), prefix);
            else if (IPAddress.TryParse(exc, out var ip) && ip.AddressFamily == family)
                yield return (ip.ToString(), maxPrefix);
        }
    }

    private static List<(string Network, int Prefix)> ApplyExclusions(
        IReadOnlyList<(string Network, int Prefix)> ranges,
        IReadOnlyList<(string Network, int Prefix)> exclusions,
        AddressFamily family)
    {
        var result = new List<(string Network, int Prefix)>(ranges);
        foreach (var excl in exclusions)
        {
            if (!IPAddress.TryParse(excl.Network, out var exclAddr))
                continue;
            if (exclAddr.AddressFamily != family)
                continue;
            result = CidrRangeHelper.SubtractCidr(result, exclAddr, excl.Prefix);
        }

        return result;
    }

    private static string FormatRanges(IReadOnlyList<(string Network, int Prefix)> ranges)
    {
        if (ranges.Count == 0)
            return "";
        return string.Join(",", ranges.Select(r => $"{r.Network}/{r.Prefix}"));
    }
}
