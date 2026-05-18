using System.Security.Cryptography;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Licensing;
using Xunit;

namespace RunFence.Tests;

public class LicenseValidatorTests
{
    // Shared test key fixture - same key used by LicenseServiceTests to avoid duplication.
    private static readonly byte[] TestPublicKeyBytes = LicenseTestKey.PublicKeyBytes;
    private static readonly LicenseValidator Validator = new(TestPublicKeyBytes);

    private static readonly byte[] TestMachineHash = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C];

    private static string BuildKey(
        byte? version = null,
        byte[]? machineHash = null,
        uint expiryDays = 0,
        LicenseTier tier = LicenseTier.Annual,
        string licenseeName = "Test User",
        ECDsa? signingKey = null)
    {
        using var ownedKey = signingKey == null ? LicenseTestKey.CreateSigningKey() : null;
        return TestKeyBuilder.BuildKey(
            signingKey ?? ownedKey!,
            machineHash ?? TestMachineHash,
            version, expiryDays, tier, licenseeName);
    }

    [Fact]
    public void ValidKey_MatchingMachine_FutureExpiry_ReturnsValid()
    {
        var now = DateTime.Today;
        var expiryDays = (uint)(now - new DateTime(2000, 1, 1)).TotalDays + 30;
        var key = BuildKey(expiryDays: expiryDays);
        var (result, info) = Validator.Validate(key, TestMachineHash, now);
        Assert.Equal(LicenseActivationResult.Success, result);
        Assert.True(info.IsValid);
    }

    [Fact]
    public void ValidKey_PastExpiry_ReturnsExpired()
    {
        var now = DateTime.Today;
        var expiryDays = (uint)Math.Max(1, (now - new DateTime(2000, 1, 1)).TotalDays - 1);
        var key = BuildKey(expiryDays: expiryDays);
        var (result, _) = Validator.Validate(key, TestMachineHash, now);
        Assert.Equal(LicenseActivationResult.Expired, result);
    }

    [Fact]
    public void ValidKey_WrongMachineId_ReturnsWrongMachine()
    {
        var now = DateTime.Today;
        var key = BuildKey();
        var wrongHash = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        var (result, _) = Validator.Validate(key, wrongHash, now);
        Assert.Equal(LicenseActivationResult.WrongMachine, result);
    }

    [Fact]
    public void ValidKey_WrongMajorVersion_ReturnsWrongVersion()
    {
        var now = DateTime.Today;
        var key = BuildKey(version: 99);
        var (result, _) = Validator.Validate(key, TestMachineHash, now);
        Assert.Equal(LicenseActivationResult.WrongVersion, result);
    }

    [Fact]
    public void TamperedKey_ModifiedPayload_ReturnsInvalidSignature()
    {
        var now = DateTime.Today;
        var key = BuildKey();
        // Flip a character near the middle
        var chars = key.ToCharArray();
        var midPos = key.Length / 2;
        chars[midPos] = chars[midPos] == 'A' ? 'B' : 'A';
        var tampered = new string(chars);
        var (result, _) = Validator.Validate(tampered, TestMachineHash, now);
        Assert.Equal(LicenseActivationResult.InvalidSignature, result);
    }

    [Fact]
    public void LifetimeKey_ZeroExpiry_ValidRegardlessOfDate()
    {
        var now = DateTime.Today;
        var key = BuildKey(expiryDays: 0, tier: LicenseTier.Lifetime);
        var (result, info) = Validator.Validate(key, TestMachineHash, now.AddYears(50));
        Assert.Equal(LicenseActivationResult.Success, result);
        Assert.True(info.IsValid);
        Assert.Null(info.ExpiryDate);
    }

    [Fact]
    public void GetLicenseInfo_ValidKey_ExtractsLicenseeName()
    {
        var now = DateTime.Today;
        var key = BuildKey(licenseeName: "Acme Corp");
        var (result, info) = Validator.Validate(key, TestMachineHash, now);
        Assert.Equal(LicenseActivationResult.Success, result);
        Assert.Equal("Acme Corp", info.LicenseeName);
    }

    [Fact]
    public void GetLicenseInfo_ValidKey_ExtractsTier()
    {
        var now = DateTime.Today;
        var key = BuildKey(tier: LicenseTier.Quarterly);
        var (result, info) = Validator.Validate(key, TestMachineHash, now);
        Assert.Equal(LicenseActivationResult.Success, result);
        Assert.Equal(LicenseTier.Quarterly, info.Tier);
    }

    [Fact]
    public void GetLicenseInfo_ValidKey_ExtractsExpiryDate()
    {
        var now = DateTime.Today;
        var epoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var targetDate = now.AddDays(30);
        var expiryDays = (uint)(targetDate - epoch).TotalDays;
        var key = BuildKey(expiryDays: expiryDays);
        var (_, info) = Validator.Validate(key, TestMachineHash, now);
        Assert.Equal(targetDate.Date, info.ExpiryDate?.Date);
    }

    [Fact]
    public void GetLicenseInfo_DaysRemaining_ComputedCorrectly()
    {
        var now = DateTime.Today;
        var epoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var targetDate = now.AddDays(10);
        var expiryDays = (uint)(targetDate - epoch).TotalDays;
        var key = BuildKey(expiryDays: expiryDays);
        var (_, info) = Validator.Validate(key, TestMachineHash, now);
        Assert.Equal(10, info.DaysRemaining);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateKey_NullOrEmpty_ReturnsMalformed(string? keyStr)
    {
        var now = DateTime.Today;
        var (result, _) = Validator.Validate(keyStr, TestMachineHash, now);
        Assert.Equal(LicenseActivationResult.Malformed, result);
    }

    [Fact]
    public void ValidateKey_TruncatedPayload_ReturnsMalformed()
    {
        var now = DateTime.Today;
        var (result, _) = Validator.Validate("RAME-AAAAAAA", TestMachineHash, now);
        Assert.Equal(LicenseActivationResult.Malformed, result);
    }

    [Fact]
    public void ValidateKey_MaxLengthName_Succeeds()
    {
        var now = DateTime.Today;
        var longName = new string('X', 255);
        var key = BuildKey(licenseeName: longName);
        var (result, info) = Validator.Validate(key, TestMachineHash, now);
        Assert.Equal(LicenseActivationResult.Success, result);
        Assert.Equal(longName, info.LicenseeName);
    }

    [Fact]
    public void ValidateKey_ZeroLengthName_Succeeds()
    {
        var now = DateTime.Today;
        var key = BuildKey(licenseeName: "");
        var (result, info) = Validator.Validate(key, TestMachineHash, now);
        Assert.Equal(LicenseActivationResult.Success, result);
        Assert.Equal("", info.LicenseeName);
    }

    [Fact]
    public void ValidateKey_Utf8MultiByteNames_ExtractedCorrectly()
    {
        var now = DateTime.Today;
        var name = "Привет мир 你好"; // Cyrillic + Chinese
        var key = BuildKey(licenseeName: name);
        var (result, info) = Validator.Validate(key, TestMachineHash, now);
        Assert.Equal(LicenseActivationResult.Success, result);
        Assert.Equal(name, info.LicenseeName);
    }

    [Fact]
    public void ValidateKey_WrongSigningKey_ReturnsInvalidSignature()
    {
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = DateTime.Today;
        var key = BuildKey(signingKey: otherKey);
        var (result, _) = Validator.Validate(key, TestMachineHash, now);
        Assert.Equal(LicenseActivationResult.InvalidSignature, result);
    }

    [Fact]
    public void ValidateKey_NameLengthExceedsPayload_ReturnsMalformed()
    {
        var now = DateTime.Today;
        // Build a payload where nameLen claims 100 bytes but only 2 actual name bytes are present.
        // We sign it with the test key so it passes signature verification, then expect Malformed
        // when the name-length bounds check fires.
        var nameBytes = new byte[] { 0x41, 0x42 }; // "AB" - only 2 bytes
        var payload = new List<byte> { EvaluationConstants.MajorVersion };
        payload.AddRange(TestMachineHash);
        payload.AddRange(BitConverter.GetBytes((uint)0)); // lifetime
        payload.Add((byte)LicenseTier.Annual);
        payload.Add(100); // nameLen = 100 but only 2 bytes follow
        payload.AddRange(nameBytes);

        var payloadBytes = payload.ToArray();
        using var signingKey = LicenseTestKey.CreateSigningKey();
        var signature = signingKey.SignData(payloadBytes, HashAlgorithmName.SHA256);
        var combined = payloadBytes.Concat(signature).ToArray();
        var base32 = MachineIdProvider.Base32Encode(combined);
        var key = "RAME-" + base32;

        var (result, _) = Validator.Validate(key, TestMachineHash, now);
        Assert.Equal(LicenseActivationResult.Malformed, result);
    }
}
