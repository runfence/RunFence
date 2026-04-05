using System.Text.RegularExpressions;
using RunFence.Licensing;
using Xunit;

namespace RunFence.Tests;

public class MachineIdProviderTests
{
    [Fact]
    public void MachineCode_KnownUuid_ProducesExpectedCode()
    {
        var provider = new MachineIdProvider("6B29FC40-CA47-1067-B31D-00DD010662DA");
        Assert.Equal("DAJ2S-IRR6C-K5UWE-7WRRQ", provider.MachineCode);
    }

    [Fact]
    public void MachineCode_DifferentUuids_ProduceDifferentCodes()
    {
        var p1 = new MachineIdProvider("11111111-1111-1111-1111-111111111111");
        var p2 = new MachineIdProvider("22222222-2222-2222-2222-222222222222");
        Assert.NotEqual(p1.MachineCode, p2.MachineCode);
    }

    [Fact]
    public void MachineCode_MatchesExpectedFormat()
    {
        var provider = new MachineIdProvider("6B29FC40-CA47-1067-B31D-00DD010662DA");
        var code = provider.MachineCode;
        // Format: XXXXX-XXXXX-XXXXX-XXXXX (4 groups of 5 base32 chars)
        Assert.Matches(new Regex(@"^[A-Z2-7]{5}-[A-Z2-7]{5}-[A-Z2-7]{5}-[A-Z2-7]{5}$"), code);
    }

    [Fact]
    public void MachineIdHash_KnownUuid_ProducesTwelveBytesHash()
    {
        var provider = new MachineIdProvider("6B29FC40-CA47-1067-B31D-00DD010662DA");
        Assert.Equal(12, provider.MachineIdHash.Length);
    }

    [Fact]
    public void ComputeHash_IsCaseInsensitive()
    {
        var h1 = MachineIdProvider.ComputeHash("abcdef-1234");
        var h2 = MachineIdProvider.ComputeHash("ABCDEF-1234");
        Assert.Equal(h1, h2);
    }
}