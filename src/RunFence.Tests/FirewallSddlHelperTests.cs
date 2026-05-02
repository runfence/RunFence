using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class FirewallSddlHelperTests
{
    private const string StandardSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string WellKnownSid = "S-1-1-0";

    // --- BuildSddl ---

    [Fact]
    public void BuildSddl_StandardSid_ProducesCorrectFormat()
    {
        var result = FirewallSddlHelper.BuildSddl(StandardSid);

        Assert.Equal($"D:(A;;CC;;;{StandardSid})", result);
    }

    [Fact]
    public void BuildSddl_WellKnownSid_ProducesCorrectFormat()
    {
        var result = FirewallSddlHelper.BuildSddl(WellKnownSid);

        Assert.Equal($"D:(A;;CC;;;{WellKnownSid})", result);
    }

    [Theory]
    [InlineData("S-1-5-21-111-222-333-1001")]
    [InlineData("S-1-5-18")]
    [InlineData("S-1-5-32-544")]
    public void BuildSddl_VariousSids_SidAppearsInResult(string sid)
    {
        var result = FirewallSddlHelper.BuildSddl(sid);

        Assert.Contains(sid, result, StringComparison.Ordinal);
        Assert.StartsWith("D:(A;;CC;;;", result, StringComparison.Ordinal);
        Assert.EndsWith(")", result, StringComparison.Ordinal);
    }

    // --- ExtractSid ---

    [Theory]
    [InlineData(StandardSid)]
    [InlineData(WellKnownSid)]
    [InlineData("S-1-5-18")]
    public void ExtractSid_ValidSddl_ReturnsSid(string sid)
    {
        var sddl = $"D:(A;;CC;;;{sid})";

        var result = FirewallSddlHelper.ExtractSid(sddl);

        Assert.Equal(sid, result);
    }

    [Fact]
    public void ExtractSid_EmptyString_ReturnsNull()
    {
        var result = FirewallSddlHelper.ExtractSid(string.Empty);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("D:(A;;CC;;;)")]
    [InlineData("D:(D;;CC;;;S-1-5-21-111-222-333-1001)")]
    [InlineData("unrelated string")]
    [InlineData("D:(A;;RC;;;S-1-5-21-111-222-333-1001)")]
    public void ExtractSid_NonMatchingSddl_ReturnsNull(string sddl)
    {
        var result = FirewallSddlHelper.ExtractSid(sddl);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractSid_BuildSddlOutput_ReturnsCorrectSid()
    {
        // Verify that the regex pattern exactly matches BuildSddl output
        var sid = "S-1-5-21-999999999-999999999-999999999-9999";
        var sddl = FirewallSddlHelper.BuildSddl(sid);

        var result = FirewallSddlHelper.ExtractSid(sddl);

        Assert.Equal(sid, result);
    }

    [Fact]
    public void ExtractSid_MultipleAces_ReturnsFirstMatchingSid()
    {
        // The regex matches the first D:(A;;CC;;;...) pattern
        var sid1 = "S-1-5-21-111-111-111-1001";
        var sddl = $"D:(A;;CC;;;{sid1})(A;;CC;;;S-1-1-0)";

        var result = FirewallSddlHelper.ExtractSid(sddl);

        Assert.Equal(sid1, result);
    }
}
