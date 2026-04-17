using System.Net;
using System.Numerics;
using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class FirewallAddressRangeBuilderTests
{
    private readonly FirewallAddressRangeBuilder _builder = new();

    // --- LAN range constants ---

    [Fact]
    public void BuildLanIPv4Range_ReturnsFixedString()
    {
        Assert.Equal("10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,169.254.0.0/16,100.64.0.0/10", _builder.BuildLanIPv4Range());
    }

    [Fact]
    public void BuildLanIPv6Range_ReturnsFixedString()
    {
        Assert.Equal("fe80::/10,fc00::/7", _builder.BuildLanIPv6Range());
    }

    // --- Internet IPv4 range ---

    [Fact]
    public void BuildInternetIPv4Range_NoExclusions_CoversInternetButNotLoopbackOrLan()
    {
        var result = _builder.BuildInternetIPv4Range([]);

        // Loopback must be excluded
        Assert.False(Contains(result, "127.0.0.1"), "Should not contain loopback 127.0.0.1");
        // RFC1918 LAN ranges must be excluded (internet rules never block LAN)
        Assert.False(Contains(result, "10.0.0.1"), "Should not contain 10.x.x.x (RFC1918)");
        Assert.False(Contains(result, "172.16.0.1"), "Should not contain 172.16.x.x (RFC1918)");
        Assert.False(Contains(result, "192.168.0.1"), "Should not contain 192.168.x.x (RFC1918)");
        // Public internet IPs must be covered
        Assert.True(Contains(result, "1.1.1.1"), "Should cover Cloudflare DNS 1.1.1.1");
        Assert.True(Contains(result, "8.8.8.8"), "Should cover Google DNS 8.8.8.8");
    }

    [Fact]
    public void BuildInternetIPv4Range_WithSingleIpExclusion_ExcludesThatIpOnly()
    {
        var result = _builder.BuildInternetIPv4Range(["8.8.8.8"]);

        Assert.False(Contains(result, "8.8.8.8"), "Excluded IP 8.8.8.8 should not be covered");
        Assert.True(Contains(result, "1.1.1.1"), "Non-excluded internet IP 1.1.1.1 should still be covered");
    }

    [Fact]
    public void BuildInternetIPv4Range_WithCidrExclusion_ExcludesEntireCidr()
    {
        var result = _builder.BuildInternetIPv4Range(["8.8.0.0/16"]);

        // All addresses in 8.8.0.0/16 should be excluded
        Assert.False(Contains(result, "8.8.0.1"), "8.8.0.1 in excluded CIDR should not be covered");
        Assert.False(Contains(result, "8.8.8.8"), "8.8.8.8 in excluded CIDR should not be covered");
        Assert.False(Contains(result, "8.8.255.254"), "8.8.255.254 in excluded CIDR should not be covered");
        // Adjacent ranges should still be covered
        Assert.True(Contains(result, "8.7.255.255"), "8.7.x.x should still be covered");
        Assert.True(Contains(result, "8.9.0.1"), "8.9.x.x should still be covered");
    }

    [Fact]
    public void BuildInternetIPv4Range_WithMultipleExclusions_ExcludesAll()
    {
        var result = _builder.BuildInternetIPv4Range(["8.8.8.8", "1.1.1.1"]);

        Assert.False(Contains(result, "8.8.8.8"));
        Assert.False(Contains(result, "1.1.1.1"));
        Assert.True(Contains(result, "9.9.9.9"), "Other internet IPs should still be covered");
    }

    [Fact]
    public void BuildInternetIPv4Range_WithIpv6Exclusions_IgnoresThemForIpv4()
    {
        // IPv6 exclusions should be silently ignored when building the IPv4 range
        var result = _builder.BuildInternetIPv4Range(["2001:4860:4860::8888"]);

        Assert.True(Contains(result, "8.8.8.8"), "IPv4 range unaffected by IPv6 exclusion");
    }

    // --- Internet IPv6 range ---

    [Fact]
    public void BuildInternetIPv6Range_NoExclusions_CoversInternetButNotLoopbackOrLan()
    {
        var result = _builder.BuildInternetIPv6Range([]);

        // Loopback must be excluded
        Assert.False(Contains(result, "::1"), "Should not contain IPv6 loopback ::1");
        // LAN ranges must be excluded (link-local and ULA)
        Assert.False(Contains(result, "fe80::1"), "Should not contain link-local fe80::1");
        Assert.False(Contains(result, "fc00::1"), "Should not contain ULA fc00::1");
        Assert.False(Contains(result, "fd00::1"), "Should not contain ULA fd00::1 (within fc00::/7)");
        // Public IPv6 addresses must be covered
        Assert.True(Contains(result, "2001:4860:4860::8888"), "Should cover Google IPv6 DNS");
        Assert.True(Contains(result, "2606:4700:4700::1111"), "Should cover Cloudflare IPv6 DNS");
    }

    [Fact]
    public void BuildInternetIPv6Range_WithSingleIpv6Exclusion_ExcludesThatIpOnly()
    {
        var result = _builder.BuildInternetIPv6Range(["2001:4860:4860::8888"]);

        Assert.False(Contains(result, "2001:4860:4860::8888"), "Excluded IPv6 address should not be covered");
        Assert.True(Contains(result, "2606:4700:4700::1111"), "Non-excluded IPv6 address should still be covered");
    }

    [Fact]
    public void BuildInternetIPv6Range_WithCidrExclusion_ExcludesEntireCidr()
    {
        var result = _builder.BuildInternetIPv6Range(["2001:4860::/32"]);

        Assert.False(Contains(result, "2001:4860::1"), "Address in excluded CIDR should not be covered");
        Assert.False(Contains(result, "2001:4860:4860::8888"), "Google DNS in excluded CIDR should not be covered");
        Assert.True(Contains(result, "2001:4861::1"), "Adjacent /32 block should still be covered");
    }

    [Fact]
    public void BuildInternetIPv6Range_WithIpv4Exclusions_IgnoresThemForIpv6()
    {
        // IPv4 exclusions should be silently ignored when building the IPv6 range
        var result = _builder.BuildInternetIPv6Range(["8.8.8.8"]);

        Assert.True(Contains(result, "2001:4860:4860::8888"), "IPv6 range unaffected by IPv4 exclusion");
    }

    // --- Helper ---

    /// <summary>
    /// Parses a comma-separated CIDR list and checks whether any range contains the given IP address.
    /// Uses the same BigInteger arithmetic as <see cref="FirewallAddressRangeBuilder"/> internally.
    /// </summary>
    private static bool Contains(string cidrList, string ip)
    {
        if (string.IsNullOrEmpty(cidrList))
            return false;
        if (!IPAddress.TryParse(ip, out var addr))
            return false;

        var addrInt = ToBigInteger(addr.GetAddressBytes());

        foreach (var entry in cidrList.Split(','))
        {
            var slash = entry.LastIndexOf('/');
            if (slash < 0)
                continue;
            if (!IPAddress.TryParse(entry[..slash], out var netAddr))
                continue;
            if (netAddr.AddressFamily != addr.AddressFamily)
                continue;
            if (!int.TryParse(entry[(slash + 1)..], out var prefix))
                continue;

            var netBytes = netAddr.GetAddressBytes();
            var netInt = ToBigInteger(netBytes);
            int maxBits = netBytes.Length * 8;

            BigInteger mask = prefix == 0
                ? BigInteger.Zero
                : ~((BigInteger.One << (maxBits - prefix)) - 1) & ((BigInteger.One << maxBits) - 1);

            if ((addrInt & mask) == (netInt & mask))
                return true;
        }

        return false;
    }

    private static BigInteger ToBigInteger(byte[] bytes)
    {
        return bytes.Aggregate(BigInteger.Zero, (current, b) => (current << 8) | b);
    }
}