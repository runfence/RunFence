using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class DynamicPortRangeCheckerTests
{
    [Fact]
    public void ParseDynamicPortRange_StandardEnglishOutput_ReturnsCorrectValues()
    {
        const string output = """
            Protocol tcp Dynamic Port Range
            ---------------------------------
            Start Port      : 49152
            Number of Ports : 16384
            """;

        var (start, count) = DynamicPortRangeChecker.ParseDynamicPortRange(output);

        Assert.Equal(49152, start);
        Assert.Equal(16384, count);
    }

    [Fact]
    public void ParseDynamicPortRange_NonStandardRange_ReturnsCorrectValues()
    {
        const string output = """
            Protocol tcp Dynamic Port Range
            ---------------------------------
            Start Port      : 1024
            Number of Ports : 64511
            """;

        var (start, count) = DynamicPortRangeChecker.ParseDynamicPortRange(output);

        Assert.Equal(1024, start);
        Assert.Equal(64511, count);
    }

    [Fact]
    public void ParseDynamicPortRange_GermanLocale_ReturnsCorrectValues()
    {
        const string output = """
            Protokoll tcp Dynamischer Portbereich
            ---------------------------------
            Startport       : 49152
            Anzahl von Ports: 16384
            """;

        var (start, count) = DynamicPortRangeChecker.ParseDynamicPortRange(output);

        Assert.Equal(49152, start);
        Assert.Equal(16384, count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no numbers here at all")]
    public void ParseDynamicPortRange_UnparsableOutput_ReturnsFallbackDefaults(string output)
    {
        var (start, count) = DynamicPortRangeChecker.ParseDynamicPortRange(output);

        Assert.Equal(DynamicPortRangeChecker.StandardEphemeralStart, start);
        Assert.Equal(DynamicPortRangeChecker.StandardEphemeralCount, count);
    }

    [Fact]
    public void ReadIPv4TcpDynamicPortRange_ReturnsValidRange()
    {
        var (start, count) = DynamicPortRangeChecker.ReadIPv4TcpDynamicPortRange();

        Assert.InRange(start, 1, 65535);
        Assert.True(count >= 1);
    }

    [Fact]
    public void ReadIPv6TcpDynamicPortRange_ReturnsValidRange()
    {
        var (start, count) = DynamicPortRangeChecker.ReadIPv6TcpDynamicPortRange();

        Assert.InRange(start, 1, 65535);
        Assert.True(count >= 1);
    }
}
