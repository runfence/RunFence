using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class HkdfKeyDerivationTests
{
    private readonly byte[] _key;

    public HkdfKeyDerivationTests()
    {
        _key = new byte[32];
        new Random(42).NextBytes(_key);
    }

    [Fact]
    public void DeriveDpapiEntropy_Deterministic()
    {
        var result1 = HkdfKeyDerivation.DeriveDpapiEntropy(_key);
        var result2 = HkdfKeyDerivation.DeriveDpapiEntropy(_key);

        Assert.Equal(result1, result2);
    }

    [Fact]
    public void DeriveConfigEncryptionKey_Deterministic()
    {
        var result1 = HkdfKeyDerivation.DeriveConfigEncryptionKey(_key);
        var result2 = HkdfKeyDerivation.DeriveConfigEncryptionKey(_key);

        Assert.Equal(result1, result2);
    }

    [Fact]
    public void DeriveCanaryEncryptionKey_Deterministic()
    {
        var result1 = HkdfKeyDerivation.DeriveCanaryEncryptionKey(_key);
        var result2 = HkdfKeyDerivation.DeriveCanaryEncryptionKey(_key);

        Assert.Equal(result1, result2);
    }

    [Fact]
    public void AllThreeDomainsProduceDifferentKeys()
    {
        var dpapiEntropy = HkdfKeyDerivation.DeriveDpapiEntropy(_key);
        var configKey = HkdfKeyDerivation.DeriveConfigEncryptionKey(_key);
        var canaryKey = HkdfKeyDerivation.DeriveCanaryEncryptionKey(_key);

        Assert.NotEqual(dpapiEntropy, configKey);
        Assert.NotEqual(dpapiEntropy, canaryKey);
        Assert.NotEqual(configKey, canaryKey);
    }

    [Fact]
    public void DifferentInputsProduceDifferentKeys()
    {
        var key2 = new byte[32];
        new Random(99).NextBytes(key2);

        var result1 = HkdfKeyDerivation.DeriveDpapiEntropy(_key);
        var result2 = HkdfKeyDerivation.DeriveDpapiEntropy(key2);

        Assert.NotEqual(result1, result2);
    }

    [Fact]
    public void OutputLength_Is32Bytes()
    {
        Assert.Equal(32, HkdfKeyDerivation.DeriveDpapiEntropy(_key).Length);
        Assert.Equal(32, HkdfKeyDerivation.DeriveConfigEncryptionKey(_key).Length);
        Assert.Equal(32, HkdfKeyDerivation.DeriveCanaryEncryptionKey(_key).Length);
    }
}