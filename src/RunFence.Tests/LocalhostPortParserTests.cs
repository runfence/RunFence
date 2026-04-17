using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class LocalhostPortParserTests
{
    // ── ParsePort (single port only) ─────────────────────────────────────

    [Theory]
    [InlineData("1", 1)]
    [InlineData("53", 53)]
    [InlineData("8080", 8080)]
    [InlineData("65535", 65535)]
    [InlineData("  8080  ", 8080)]
    public void ParsePort_ValidInput_ReturnsPort(string input, int expected)
    {
        Assert.Equal(expected, LocalhostPortParser.ParsePort(input));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("8080a")]
    [InlineData("80.80")]
    public void ParsePort_InvalidInput_ReturnsNull(string input)
    {
        Assert.Null(LocalhostPortParser.ParsePort(input));
    }

    // ── ParsePortOrRange ─────────────────────────────────────────────────

    [Theory]
    [InlineData("53", 53, 53)]
    [InlineData("8080", 8080, 8080)]
    [InlineData("8080-8090", 8080, 8090)]
    [InlineData("1-65535", 1, 65535)]
    [InlineData("  3000 - 3010  ", 3000, 3010)]
    public void ParsePortOrRange_ValidInput_ReturnsRange(string input, int low, int high)
    {
        Assert.Equal(new PortRange(low, high), LocalhostPortParser.ParsePortOrRange(input));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("8090-8080")]       // low > high
    [InlineData("0-100")]           // low out of range
    [InlineData("100-65536")]       // high out of range
    [InlineData("-")]               // just a dash
    [InlineData("8080-")]           // trailing dash
    [InlineData("-8080")]           // leading dash (ambiguous with negative)
    [InlineData("80-90-100")]       // multiple dashes — second dash causes parse failure
    public void ParsePortOrRange_InvalidInput_ReturnsNull(string input)
    {
        Assert.Null(LocalhostPortParser.ParsePortOrRange(input));
    }

    // ── CoalescePortRanges ───────────────────────────────────────────────

    [Fact]
    public void CoalescePortRanges_Empty_ReturnsEmpty()
    {
        Assert.Empty(LocalhostPortParser.CoalescePortRanges([]));
    }

    [Fact]
    public void CoalescePortRanges_SinglePort_ReturnsSingleRange()
    {
        Assert.Equal([new PortRange(55000, 55000)], LocalhostPortParser.CoalescePortRanges([55000]));
    }

    [Fact]
    public void CoalescePortRanges_TwoAdjacentPorts_MergedIntoRange()
    {
        Assert.Equal([new PortRange(55000, 55001)], LocalhostPortParser.CoalescePortRanges([55000, 55001]));
    }

    [Fact]
    public void CoalescePortRanges_TwoNonAdjacentPorts_SeparateRanges()
    {
        Assert.Equal([new PortRange(55000, 55000), new PortRange(56000, 56000)],
            LocalhostPortParser.CoalescePortRanges([55000, 56000]));
    }

    [Fact]
    public void CoalescePortRanges_UnsortedInput_SortedAndMerged()
    {
        Assert.Equal([new PortRange(55000, 55001)], LocalhostPortParser.CoalescePortRanges([55001, 55000]));
    }

    [Fact]
    public void CoalescePortRanges_LargeGapBetweenPorts_TwoSeparateRanges()
    {
        Assert.Equal([new PortRange(50000, 50000), new PortRange(60000, 60000)],
            LocalhostPortParser.CoalescePortRanges([50000, 60000]));
    }

    // ── PortRange.ToString ───────────────────────────────────────────────

    [Fact]
    public void PortRange_SinglePort_ToStringReturnsPort()
    {
        Assert.Equal("53", new PortRange(53, 53).ToString());
    }

    [Fact]
    public void PortRange_Range_ToStringReturnsRange()
    {
        Assert.Equal("8080-8090", new PortRange(8080, 8090).ToString());
    }
}
