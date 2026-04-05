using System.Net;
using System.Net.Sockets;
using System.Numerics;

namespace RunFence.Firewall;

/// <summary>
/// Computes Windows Firewall RemoteAddress range strings by performing CIDR subtraction.
/// Non-static for testability.
/// </summary>
public class FirewallAddressRangeBuilder
{
    // IPv4 base: everything except loopback and LAN
    private static readonly (string Network, int Prefix)[] IPv4BaseRanges =
    [
        ("0.0.0.0", 0)
    ];

    private static readonly (string Network, int Prefix)[] IPv4BaseExclusions =
    [
        ("127.0.0.0", 8),
        ("10.0.0.0", 8),
        ("172.16.0.0", 12),
        ("192.168.0.0", 16)
    ];

    // IPv6 base: everything except loopback and LAN
    private static readonly (string Network, int Prefix)[] IPv6BaseRanges =
    [
        ("::", 0)
    ];

    private static readonly (string Network, int Prefix)[] IPv6BaseExclusions =
    [
        ("::1", 128),
        ("fe80::", 10),
        ("fc00::", 7)
    ];

    // LAN base ranges
    private static readonly (string Network, int Prefix)[] IPv4LanBaseRanges =
    [
        ("10.0.0.0", 8),
        ("172.16.0.0", 12),
        ("192.168.0.0", 16)
    ];

    private static readonly (string Network, int Prefix)[] IPv6LanBaseRanges =
    [
        ("fe80::", 10),
        ("fc00::", 7)
    ];

    /// <summary>
    /// Returns comma-separated IPv4 internet block range with given IPs excluded.
    /// </summary>
    public string BuildInternetIPv4Range(IReadOnlyList<string> exclusions)
    {
        var ranges = ApplyExclusions(IPv4BaseRanges.ToList(), IPv4BaseExclusions.Concat(
            ParseIPv4Exclusions(exclusions)).ToList(), AddressFamily.InterNetwork);
        return FormatRanges(ranges);
    }

    /// <summary>
    /// Returns comma-separated IPv6 internet block range with given IPs excluded.
    /// </summary>
    public string BuildInternetIPv6Range(IReadOnlyList<string> exclusions)
    {
        var ranges = ApplyExclusions(IPv6BaseRanges.ToList(), IPv6BaseExclusions.Concat(
            ParseIPv6Exclusions(exclusions)).ToList(), AddressFamily.InterNetworkV6);
        return FormatRanges(ranges);
    }

    /// <summary>Returns fixed IPv4 LAN ranges.</summary>
    public string BuildLanIPv4Range() => "10.0.0.0/8,172.16.0.0/12,192.168.0.0/16";

    /// <summary>Returns fixed IPv6 LAN ranges.</summary>
    public string BuildLanIPv6Range() => "fe80::/10,fc00::/7";

    /// <summary>
    /// Returns comma-separated IPv4 LAN block range with given IPs excluded.
    /// </summary>
    public string BuildLanIPv4Range(IReadOnlyList<string> exclusions)
    {
        var ranges = ApplyExclusions(IPv4LanBaseRanges.ToList(),
            ParseIPv4Exclusions(exclusions).ToList(), AddressFamily.InterNetwork);
        return FormatRanges(ranges);
    }

    /// <summary>
    /// Returns comma-separated IPv6 LAN block range with given IPs excluded.
    /// </summary>
    public string BuildLanIPv6Range(IReadOnlyList<string> exclusions)
    {
        var ranges = ApplyExclusions(IPv6LanBaseRanges.ToList(),
            ParseIPv6Exclusions(exclusions).ToList(), AddressFamily.InterNetworkV6);
        return FormatRanges(ranges);
    }

    private static IEnumerable<(string Network, int Prefix)> ParseIPv4Exclusions(IReadOnlyList<string> exclusions)
    {
        foreach (var exc in exclusions)
        {
            if (TryParseCidr(exc, out var net, out var prefix) &&
                net.AddressFamily == AddressFamily.InterNetwork)
                yield return (net.ToString(), prefix);
            else if (IPAddress.TryParse(exc, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
                yield return (ip.ToString(), 32);
        }
    }

    private static IEnumerable<(string Network, int Prefix)> ParseIPv6Exclusions(IReadOnlyList<string> exclusions)
    {
        foreach (var exc in exclusions)
        {
            if (TryParseCidr(exc, out var net, out var prefix) &&
                net.AddressFamily == AddressFamily.InterNetworkV6)
                yield return (net.ToString(), prefix);
            else if (IPAddress.TryParse(exc, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6)
                yield return (ip.ToString(), 128);
        }
    }

    private static bool TryParseCidr(string cidr, out IPAddress network, out int prefix)
    {
        network = IPAddress.None;
        prefix = 0;
        var slash = cidr.LastIndexOf('/');
        if (slash < 0)
            return false;
        if (!IPAddress.TryParse(cidr[..slash], out var net))
            return false;
        if (!int.TryParse(cidr[(slash + 1)..], out prefix))
            return false;
        var maxBits = net.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (prefix < 0 || prefix > maxBits)
            return false;
        // Mask out host bits to get canonical network address
        network = MaskToPrefix(net, prefix, maxBits);
        return true;
    }

    private static List<(string Network, int Prefix)> ApplyExclusions(
        List<(string Network, int Prefix)> ranges,
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
            result = SubtractCidr(result, exclAddr, excl.Prefix, family);
        }

        return result;
    }

    private static List<(string Network, int Prefix)> SubtractCidr(
        List<(string Network, int Prefix)> ranges,
        IPAddress exclNetwork,
        int exclPrefix,
        AddressFamily family)
    {
        var result = new List<(string Network, int Prefix)>();
        int maxBits = family == AddressFamily.InterNetwork ? 32 : 128;

        foreach (var (netStr, prefix) in ranges)
        {
            if (!IPAddress.TryParse(netStr, out var netAddr))
                continue;

            if (!CidrContains(netAddr, prefix, exclNetwork, exclPrefix, maxBits))
            {
                // No overlap — keep as-is
                result.Add((netStr, prefix));
                continue;
            }

            if (prefix >= exclPrefix)
            {
                // Exclusion is larger than or equal to this block — this entire block is excluded
                continue;
            }

            // Split this block: remove exclNetwork/exclPrefix, keep the remainder as sub-blocks
            result.AddRange(SplitAroundExclusion(netAddr, prefix, exclNetwork, exclPrefix, maxBits));
        }

        return result;
    }

    /// <summary>
    /// Returns sub-CIDRs of (blockNetwork/blockPrefix) that do NOT overlap with (exclNetwork/exclPrefix).
    /// Standard CIDR subtraction: walk from blockPrefix+1 to exclPrefix, emitting sibling blocks.
    /// </summary>
    private static IEnumerable<(string Network, int Prefix)> SplitAroundExclusion(
        IPAddress blockNetwork, int blockPrefix,
        IPAddress exclNetwork, int exclPrefix,
        int maxBits)
    {
        // At each bit depth from blockPrefix+1 to exclPrefix, the sibling block of exclNetwork is kept.
        for (int p = blockPrefix + 1; p <= exclPrefix; p++)
        {
            var sibling = FlipBit(exclNetwork, p - 1, maxBits);
            var siblingNet = MaskToPrefix(sibling, p, maxBits);
            yield return (siblingNet.ToString(), p);
        }
    }

    /// <summary>
    /// Returns true if the block (blockNet/blockPrefix) and exclusion (exclNet/exclPrefix) overlap.
    /// Overlap exists when one CIDR contains the other, or they are the same.
    /// Uses the less specific (smaller) prefix as the mask to test containment.
    /// </summary>
    private static bool CidrContains(IPAddress blockNet, int blockPrefix, IPAddress exclNet, int exclPrefix, int maxBits)
    {
        var blockInt = ToInt(blockNet, maxBits);
        var exclInt = ToInt(exclNet, maxBits);
        // Test using the shorter (less specific) prefix as the mask
        int shorterPrefix = Math.Min(blockPrefix, exclPrefix);
        var mask = shorterPrefix == 0 ? BigInteger.Zero : ~((BigInteger.One << (maxBits - shorterPrefix)) - 1) & MaskMax(maxBits);
        return (blockInt & mask) == (exclInt & mask);
    }

    private static IPAddress FlipBit(IPAddress addr, int bitIndex, int maxBits)
    {
        var val = ToInt(addr, maxBits);
        var bit = BigInteger.One << (maxBits - 1 - bitIndex);
        val ^= bit;
        return FromInt(val, maxBits);
    }

    private static IPAddress MaskToPrefix(IPAddress addr, int prefix, int maxBits)
    {
        var val = ToInt(addr, maxBits);
        var mask = prefix == 0 ? BigInteger.Zero : ~((BigInteger.One << (maxBits - prefix)) - 1) & MaskMax(maxBits);
        return FromInt(val & mask, maxBits);
    }

    private static BigInteger MaskMax(int maxBits) =>
        (BigInteger.One << maxBits) - 1;

    private static BigInteger ToInt(IPAddress addr, int maxBits)
    {
        var bytes = addr.GetAddressBytes();
        if (bytes.Length * 8 < maxBits)
        {
            // Pad to required length
            var padded = new byte[maxBits / 8];
            Array.Copy(bytes, 0, padded, padded.Length - bytes.Length, bytes.Length);
            bytes = padded;
        }

        // Big-endian unsigned
        return bytes.Aggregate(BigInteger.Zero, (current, b) => (current << 8) | b);
    }

    private static IPAddress FromInt(BigInteger value, int maxBits)
    {
        int byteCount = maxBits / 8;
        var bytes = new byte[byteCount];
        for (int i = byteCount - 1; i >= 0; i--)
        {
            bytes[i] = (byte)(value & 0xFF);
            value >>= 8;
        }

        return new IPAddress(bytes);
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

    private static string FormatRanges(IReadOnlyList<(string Network, int Prefix)> ranges)
    {
        if (ranges.Count == 0)
            return "";
        return string.Join(",", ranges.Select(r => $"{r.Network}/{r.Prefix}"));
    }
}