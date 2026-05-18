using RunFence.Licensing;
using Xunit;

namespace RunFence.Tests;

public class MachineIdProviderTests
{
    [Fact]
    public void UsesSmbiosUuidWhenValid_WithExpectedGoldenHashAndMachineCode()
    {
        var provider = new MachineIdProvider(new Reader("6B29FC40-CA47-1067-B31D-00DD010662DA", "ABCDEF-1234"));

        var result = provider.GetMachineIdentity();

        Assert.Equal(MachineIdentityStatus.Available, result.Status);
        Assert.Equal(MachineIdentitySource.SmbiosUuid, result.Source);
        Assert.Equal("6B29FC40-CA47-1067-B31D-00DD010662DA", result.CanonicalSourceValue);
        Assert.Equal("1813A92231F095DA589FB463", Convert.ToHexString(result.MachineIdHash!));
        Assert.Equal("DAJ2S-IRR6C-K5UWE-7WRRQ", result.MachineCode);
        Assert.Equal(12, result.MachineIdHash!.Length);
    }

    [Fact]
    public void FallsBackToMachineGuidWhenSmbiosInvalid_WithExpectedGoldenHashAndMachineCode()
    {
        var provider = new MachineIdProvider(new Reader("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF", "ABCDEF-1234"));

        var result = provider.GetMachineIdentity();

        Assert.Equal(MachineIdentityStatus.Available, result.Status);
        Assert.Equal(MachineIdentitySource.WindowsMachineGuid, result.Source);
        Assert.Equal("ABCDEF-1234", result.CanonicalSourceValue);
        Assert.Equal("F3BAEE382F69E0E002701AE0", Convert.ToHexString(result.MachineIdHash!));
        Assert.Equal("6O5O4-OBPNH-QOAAT-QDLQA", result.MachineCode);
        Assert.Equal(12, result.MachineIdHash!.Length);
    }

    [Fact]
    public void SameRawValueFromSmbiosAndMachineGuid_ProducesIdenticalHashAndMachineCode()
    {
        const string rawValue = "11111111-1111-1111-1111-111111111111";

        var smbios = new MachineIdProvider(new Reader(rawValue, null));
        var machineGuid = new MachineIdProvider(new Reader("bad", rawValue));

        Assert.Equal("BAFDE89C041E1756082B933A", Convert.ToHexString(smbios.MachineIdHash));
        Assert.Equal("BAFDE89C041E1756082B933A", Convert.ToHexString(machineGuid.MachineIdHash));
        Assert.Equal("XL66R-HAEDY-LVMCB-LSM5A", smbios.MachineCode);
        Assert.Equal("XL66R-HAEDY-LVMCB-LSM5A", machineGuid.MachineCode);
        Assert.Equal(smbios.MachineIdHash, machineGuid.MachineIdHash);
    }

    [Fact]
    public void NormalizesSmbiosGuidCaseBeforeHashing()
    {
        var lower = new MachineIdProvider(new Reader("6b29fc40-ca47-1067-b31d-00dd010662da", null));
        var upper = new MachineIdProvider(new Reader("6B29FC40-CA47-1067-B31D-00DD010662DA", null));

        Assert.Equal(Convert.ToHexString(upper.MachineIdHash), Convert.ToHexString(lower.MachineIdHash));
        Assert.Equal(upper.MachineCode, lower.MachineCode);
    }

    [Theory]
    [InlineData("To be filled by O.E.M.")]
    [InlineData("Default string")]
    [InlineData("System Product Name")]
    [InlineData("System Serial Number")]
    [InlineData("None")]
    [InlineData("Unknown")]
    [InlineData("")]
    public void RejectsPlaceholderValues(string value)
    {
        var provider = new MachineIdProvider(new Reader(value, value));

        var result = provider.GetMachineIdentity();

        Assert.Equal(MachineIdentityStatus.Unavailable, result.Status);
    }

    private sealed class Reader(string? smbios, string? machineGuid) : IMachineIdentityReader
    {
        public string? ReadSmbiosUuid() => smbios;
        public string? ReadWindowsMachineGuid() => machineGuid;
    }
}
