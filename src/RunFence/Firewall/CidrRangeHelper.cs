using System.Net;
using System.Net.Sockets;
using System.Numerics;

namespace RunFence.Firewall;

/// <summary>
/// Pure CIDR arithmetic utilities: containment checks, bit manipulation, address conversion,
/// and CIDR subtraction. Used by <see cref="FirewallAddressRangeBuilder"/> and other callers.
/// </summary>
public static class CidrRangeHelper
{
    /// <summary>
    /// Returns true if the block (blockNet/blockPrefix) and exclusion (exclNet/exclPrefix) overlap.
    /// Overlap exists when one CIDR contains the other, or they are the same.
    /// Uses the less specific (smaller) prefix as the mask to test containment.
    /// </summary>
    public static bool CidrContains(IPAddress blockNet, int blockPrefix, IPAddress exclNet, int exclPrefix, int maxBits)
    {
        var blockInt = ToInt(blockNet, maxBits);
        var exclInt = ToInt(exclNet, maxBits);
        // Test using the shorter (less specific) prefix as the mask
        int shorterPrefix = Math.Min(blockPrefix, exclPrefix);
        var mask = shorterPrefix == 0 ? BigInteger.Zero : ~((BigInteger.One << (maxBits - shorterPrefix)) - 1) & MaskMax(maxBits);
        return (blockInt & mask) == (exclInt & mask);
    }

    public static IPAddress FlipBit(IPAddress addr, int bitIndex, int maxBits)
    {
        var val = ToInt(addr, maxBits);
        var bit = BigInteger.One << (maxBits - 1 - bitIndex);
        val ^= bit;
        return FromInt(val, maxBits);
    }

    public static IPAddress MaskToPrefix(IPAddress addr, int prefix, int maxBits)
    {
        var val = ToInt(addr, maxBits);
        var mask = prefix == 0 ? BigInteger.Zero : ~((BigInteger.One << (maxBits - prefix)) - 1) & MaskMax(maxBits);
        return FromInt(val & mask, maxBits);
    }

    public static BigInteger MaskMax(int maxBits) =>
        (BigInteger.One << maxBits) - 1;

    public static BigInteger ToInt(IPAddress addr, int maxBits)
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

    public static IPAddress FromInt(BigInteger value, int maxBits)
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

    /// <summary>
    /// Subtracts the CIDR exclusion from each block in <paramref name="ranges"/> and returns the
    /// resulting non-overlapping sub-blocks.
    /// </summary>
    public static List<(string Network, int Prefix)> SubtractCidr(
        IReadOnlyList<(string Network, int Prefix)> ranges,
        IPAddress exclNetwork,
        int exclPrefix)
    {
        var result = new List<(string Network, int Prefix)>();
        int maxBits = exclNetwork.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;

        foreach (var (netStr, prefix) in ranges)
        {
            if (!IPAddress.TryParse(netStr, out var netAddr))
                continue;

            if (!CidrContains(netAddr, prefix, exclNetwork, exclPrefix, maxBits))
            {
                result.Add((netStr, prefix));
                continue;
            }

            if (prefix >= exclPrefix)
                continue;

            result.AddRange(SplitAroundExclusion(prefix, exclNetwork, exclPrefix, maxBits));
        }

        return result;
    }

    /// <summary>
    /// Returns sub-CIDRs of (blockNetwork/blockPrefix) that do NOT overlap with (exclNetwork/exclPrefix).
    /// Standard CIDR subtraction: walk from blockPrefix+1 to exclPrefix, emitting sibling blocks.
    /// </summary>
    public static IEnumerable<(string Network, int Prefix)> SplitAroundExclusion(
        int blockPrefix,
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
    /// Returns true if <paramref name="ip"/> falls within the CIDR range or matches the IP exactly.
    /// Supports both IPv4 and IPv6. Returns false on any parse failure.
    /// </summary>
    public static bool IsInCidrRange(string ip, string cidr)
    {
        if (!IPAddress.TryParse(ip, out var ipAddr))
            return false;

        if (!TryParseCidr(cidr, out var network, out var prefix))
        {
            // cidr may be a plain IP — do exact match
            return IPAddress.TryParse(cidr, out var plainIp) &&
                   ipAddr.AddressFamily == plainIp.AddressFamily &&
                   ipAddr.Equals(plainIp);
        }

        if (ipAddr.AddressFamily != network.AddressFamily)
            return false;

        int maxBits = ipAddr.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        return CidrContains(network, prefix, ipAddr, maxBits, maxBits);
    }

    public static bool TryParseCidr(string cidr, out IPAddress network, out int prefix)
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
}
