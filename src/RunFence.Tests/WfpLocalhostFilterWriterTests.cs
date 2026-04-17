using RunFence.Firewall;
using RunFence.Firewall.Wfp;
using Xunit;

namespace RunFence.Tests;

public class WfpLocalhostFilterWriterTests
{
    private static PortRange P(int port) => new(port, port);
    private static PortRange R(int low, int high) => new(low, high);

    [Fact]
    public void BuildBlockedPortRanges_NoExemptions_ReturnsSingleRange()
    {
        var ranges = WfpLocalhostFilterWriter.BuildBlockedPortRanges([]);
        Assert.Equal([R(1, 65535)], ranges);
    }

    [Theory]
    [InlineData(53,    1, 52,    54, 65535)]    // typical DNS port: two ranges
    [InlineData(49151, 1, 49150, 49152, 65535)] // just below ephemeral boundary
    public void BuildBlockedPortRanges_SinglePortMiddle_ReturnsTwoRanges(
        int exemptPort, int lo1, int hi1, int lo2, int hi2)
    {
        var ranges = WfpLocalhostFilterWriter.BuildBlockedPortRanges([P(exemptPort)]);
        Assert.Equal([R(lo1, hi1), R(lo2, hi2)], ranges);
    }

    [Theory]
    [InlineData(1,     2, 65535)] // port 1 exempt: no leading range
    [InlineData(65535, 1, 65534)] // port 65535 exempt: no trailing range
    public void BuildBlockedPortRanges_SinglePortAtBoundary_ReturnsSingleRange(
        int exemptPort, int expectedLow, int expectedHigh)
    {
        var ranges = WfpLocalhostFilterWriter.BuildBlockedPortRanges([P(exemptPort)]);
        Assert.Equal([R(expectedLow, expectedHigh)], ranges);
    }

    [Fact]
    public void BuildBlockedPortRanges_TwoPorts_ReturnsThreeRanges()
    {
        var ranges = WfpLocalhostFilterWriter.BuildBlockedPortRanges([P(53), P(3000)]);
        Assert.Equal([R(1, 52), R(54, 2999), R(3001, 65535)], ranges);
    }

    [Fact]
    public void BuildBlockedPortRanges_AdjacentPorts_MergesGap()
    {
        var ranges = WfpLocalhostFilterWriter.BuildBlockedPortRanges([P(53), P(54)]);
        Assert.Equal([R(1, 52), R(55, 65535)], ranges);
    }

    [Fact]
    public void BuildBlockedPortRanges_MixedPortsIncludingEphemeral()
    {
        // Exemptions [53, 50000] — ephemeral port now included in complement
        var ranges = WfpLocalhostFilterWriter.BuildBlockedPortRanges([P(53), P(50000)]);
        Assert.Equal([R(1, 52), R(54, 49999), R(50001, 65535)], ranges);
    }

    [Fact]
    public void BuildBlockedPortRanges_UnsortedInput_SortsAutomatically()
    {
        var ranges = WfpLocalhostFilterWriter.BuildBlockedPortRanges([P(3000), P(53)]);
        Assert.Equal([R(1, 52), R(54, 2999), R(3001, 65535)], ranges);
    }

    // ── Range exemptions ─────────────────────────────────────────────────

    [Fact]
    public void BuildBlockedPortRanges_SingleRange_CreatesGap()
    {
        var ranges = WfpLocalhostFilterWriter.BuildBlockedPortRanges([R(3000, 3010)]);
        Assert.Equal([R(1, 2999), R(3011, 65535)], ranges);
    }

    [Fact]
    public void BuildBlockedPortRanges_OverlappingRanges_MergedCorrectly()
    {
        var ranges = WfpLocalhostFilterWriter.BuildBlockedPortRanges([R(50, 60), R(55, 70)]);
        Assert.Equal([R(1, 49), R(71, 65535)], ranges);
    }

    [Fact]
    public void BuildBlockedPortRanges_AdjacentRanges_MergedCorrectly()
    {
        var ranges = WfpLocalhostFilterWriter.BuildBlockedPortRanges([R(50, 60), R(61, 70)]);
        Assert.Equal([R(1, 49), R(71, 65535)], ranges);
    }

    [Fact]
    public void BuildBlockedPortRanges_RangeSpanningEphemeral_NoClamp()
    {
        // Range 49000-50000 spans the former ephemeral boundary — no longer clamped
        var ranges = WfpLocalhostFilterWriter.BuildBlockedPortRanges([R(49000, 50000)]);
        Assert.Equal([R(1, 48999), R(50001, 65535)], ranges);
    }

    [Fact]
    public void BuildBlockedPortRanges_MixedPortsAndRanges()
    {
        var ranges = WfpLocalhostFilterWriter.BuildBlockedPortRanges([P(53), R(3000, 3010), P(8080)]);
        Assert.Equal([R(1, 52), R(54, 2999), R(3011, 8079), R(8081, 65535)], ranges);
    }

    [Fact]
    public void BuildBlockedPortRanges_FullRange1To49151_ReturnsTail()
    {
        // R(1,49151) exempted — the tail 49152-65535 is still blocked
        var ranges = WfpLocalhostFilterWriter.BuildBlockedPortRanges([R(1, 49151)]);
        Assert.Equal([R(49152, 65535)], ranges);
    }

    [Fact]
    public void BuildBlockedPortRanges_FullRange1To65535_ReturnsEmpty()
    {
        var ranges = WfpLocalhostFilterWriter.BuildBlockedPortRanges([R(1, 65535)]);
        Assert.Empty(ranges);
    }
}
