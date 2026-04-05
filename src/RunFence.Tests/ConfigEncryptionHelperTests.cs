using System.Security.Cryptography;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class ConfigEncryptionHelperTests
{
    private readonly byte[] _key;
    private readonly byte[] _argonSalt;

    public ConfigEncryptionHelperTests()
    {
        _key = new byte[32];
        new Random(42).NextBytes(_key);
        _argonSalt = new byte[32];
        new Random(77).NextBytes(_argonSalt);
    }

    // --- AesGcmHelper tests ---

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(65536)]
    public void AesGcmHelper_RoundTrip_VaryingSizes(int size)
    {
        var plaintext = new byte[size];
        new Random(size).NextBytes(plaintext);

        var encrypted = AesGcmHelper.Encrypt(plaintext, _key);
        var decrypted = AesGcmHelper.Decrypt(encrypted, _key);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void AesGcmHelper_WrongKey_ThrowsCryptographicException()
    {
        var plaintext = "Hello, World!"u8.ToArray();
        var encrypted = AesGcmHelper.Encrypt(plaintext, _key);

        var wrongKey = new byte[32];
        new Random(99).NextBytes(wrongKey);

        Assert.ThrowsAny<CryptographicException>(() => AesGcmHelper.Decrypt(encrypted, wrongKey));
    }

    [Fact]
    public void AesGcmHelper_TamperedCiphertext_ThrowsCryptographicException()
    {
        var plaintext = "Test data"u8.ToArray();
        var encrypted = AesGcmHelper.Encrypt(plaintext, _key);

        // Tamper with the ciphertext portion (after nonce + tag)
        encrypted[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => AesGcmHelper.Decrypt(encrypted, _key));
    }

    [Fact]
    public void AesGcmHelper_AadMismatch_ThrowsCryptographicException()
    {
        var plaintext = "Test data"u8.ToArray();
        var aad = new byte[] { 0x01, 0x02, 0x03 };
        var wrongAad = new byte[] { 0x01, 0x02, 0x04 };

        var encrypted = AesGcmHelper.Encrypt(plaintext, _key, aad);

        Assert.ThrowsAny<CryptographicException>(() => AesGcmHelper.Decrypt(encrypted, _key, wrongAad));
    }

    [Fact]
    public void AesGcmHelper_NonceUniqueness_SamePlaintextDifferentCiphertext()
    {
        var plaintext = "Same data"u8.ToArray();

        var encrypted1 = AesGcmHelper.Encrypt(plaintext, _key);
        var encrypted2 = AesGcmHelper.Encrypt(plaintext, _key);

        // Nonces should differ (random), so ciphertext should differ
        Assert.NotEqual(encrypted1, encrypted2);
    }

    [Fact]
    public void AesGcmHelper_TooShortData_ThrowsCryptographicException()
    {
        var tooShort = new byte[5]; // Less than 12 (nonce) + 16 (tag)
        Assert.Throws<CryptographicException>(() => AesGcmHelper.Decrypt(tooShort, _key));
    }

    // --- ConfigEncryptionHelper tests ---

    [Fact]
    public void ConfigEncryptionHelper_MainConfig_RoundTrip()
    {
        var plaintext = """{"apps":[],"credentials":[]}"""u8.ToArray();

        var encrypted = ConfigEncryptionHelper.EncryptConfig(plaintext, _key, ConfigFileType.MainConfig, _argonSalt);
        var decrypted = ConfigEncryptionHelper.DecryptConfig(encrypted, _key, ConfigFileType.MainConfig);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void ConfigEncryptionHelper_AppConfig_RoundTrip()
    {
        var plaintext = """{"apps":[{"name":"TestApp"}]}"""u8.ToArray();

        var encrypted = ConfigEncryptionHelper.EncryptConfig(plaintext, _key, ConfigFileType.AppConfig, _argonSalt);
        var decrypted = ConfigEncryptionHelper.DecryptConfig(encrypted, _key, ConfigFileType.AppConfig);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void ConfigEncryptionHelper_CrossTypeSubstitution_ThrowsCryptographicException()
    {
        var plaintext = "test data"u8.ToArray();

        // Encrypt as MainConfig
        var encrypted = ConfigEncryptionHelper.EncryptConfig(plaintext, _key, ConfigFileType.MainConfig, _argonSalt);

        // Decrypt as AppConfig — should fail (different AAD)
        Assert.ThrowsAny<CryptographicException>(() =>
            ConfigEncryptionHelper.DecryptConfig(encrypted, _key, ConfigFileType.AppConfig));
    }

    [Fact]
    public void ConfigEncryptionHelper_WrongKey_ThrowsCryptographicException()
    {
        var plaintext = "test data"u8.ToArray();
        var encrypted = ConfigEncryptionHelper.EncryptConfig(plaintext, _key, ConfigFileType.MainConfig, _argonSalt);

        var wrongKey = new byte[32];
        new Random(99).NextBytes(wrongKey);

        Assert.ThrowsAny<CryptographicException>(() =>
            ConfigEncryptionHelper.DecryptConfig(encrypted, wrongKey, ConfigFileType.MainConfig));
    }

    [Theory]
    [InlineData(new byte[] { })] // empty
    [InlineData(new byte[] { 0x52, 0x41 })] // too short (RA...)
    public void ConfigEncryptionHelper_InvalidData_ThrowsCryptographicException(byte[] data)
    {
        Assert.Throws<CryptographicException>(() =>
            ConfigEncryptionHelper.DecryptConfig(data, _key, ConfigFileType.MainConfig));
    }

    [Fact]
    public void ConfigEncryptionHelper_WrongMagic_ThrowsCryptographicException()
    {
        var data = new byte[] { 0x58, 0x58, 0x58, 0x58, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00 };
        Assert.Throws<CryptographicException>(() =>
            ConfigEncryptionHelper.DecryptConfig(data, _key, ConfigFileType.MainConfig));
    }

    [Theory]
    [InlineData((byte)0x01)]
    [InlineData((byte)0xFF)]
    public void ConfigEncryptionHelper_UnsupportedVersion_ThrowsCryptographicException(byte badVersion)
    {
        var plaintext = "test"u8.ToArray();
        var encrypted = ConfigEncryptionHelper.EncryptConfig(plaintext, _key, ConfigFileType.MainConfig, _argonSalt);
        // Corrupt version byte (index 4)
        encrypted[4] = badVersion;

        Assert.Throws<CryptographicException>(() =>
            ConfigEncryptionHelper.DecryptConfig(encrypted, _key, ConfigFileType.MainConfig));
    }

    [Fact]
    public void HasEncryptionHeader_ValidRame_ReturnsTrue()
    {
        var plaintext = "test"u8.ToArray();
        var encrypted = ConfigEncryptionHelper.EncryptConfig(plaintext, _key, ConfigFileType.MainConfig, _argonSalt);

        Assert.True(ConfigEncryptionHelper.HasEncryptionHeader(encrypted));
    }

    [Fact]
    public void HasEncryptionHeader_PlaintextJson_ReturnsFalse()
    {
        var json = """{"apps":[]}"""u8.ToArray();
        Assert.False(ConfigEncryptionHelper.HasEncryptionHeader(json));
    }

    [Fact]
    public void HasEncryptionHeader_Empty_ReturnsFalse()
    {
        Assert.False(ConfigEncryptionHelper.HasEncryptionHeader([]));
    }

    [Fact]
    public void HasEncryptionHeader_TooShort_ReturnsFalse()
    {
        Assert.False(ConfigEncryptionHelper.HasEncryptionHeader([0x52, 0x41]));
    }

    // --- TryExtractArgonSalt tests ---

    public static TheoryData<byte[]> InvalidArgonSaltInputs()
    {
        var magic = new byte[] { 0x52, 0x41, 0x4D, 0x45 }; // "RAME"

        // 38-byte array with correct magic but v1 version byte
        var v1Array = new byte[38];
        Buffer.BlockCopy(magic, 0, v1Array, 0, 4);
        v1Array[4] = 0x01;

        // 38-byte array with wrong magic (correct length, correct version)
        var wrongMagic = new byte[38];
        wrongMagic[4] = 0x02;

        // 37-byte truncated v2 (magic + version 0x02 + fileType + 31 salt bytes)
        var truncated = new byte[37];
        Buffer.BlockCopy(magic, 0, truncated, 0, 4);
        truncated[4] = 0x02;

        return
        [
            [],
            magic, // magic-only (4 bytes, too short)
            v1Array, // v1 version byte
            wrongMagic, // wrong magic
            truncated
        ];
    }

    [Theory]
    [MemberData(nameof(InvalidArgonSaltInputs))]
    public void TryExtractArgonSalt_InvalidInput_ReturnsNull(byte[] input)
    {
        Assert.Null(ConfigEncryptionHelper.TryExtractArgonSalt(input));
    }

    [Fact]
    public void TryExtractArgonSalt_ValidV2File_ReturnsSalt()
    {
        var plaintext = "test"u8.ToArray();
        var encrypted = ConfigEncryptionHelper.EncryptConfig(plaintext, _key, ConfigFileType.MainConfig, _argonSalt);

        var extracted = ConfigEncryptionHelper.TryExtractArgonSalt(encrypted);

        Assert.NotNull(extracted);
        Assert.Equal(_argonSalt, extracted);
    }

    [Fact]
    public void ConfigEncryptionHelper_TamperedSalt_ThrowsCryptographicException()
    {
        var plaintext = "test data"u8.ToArray();
        var encrypted = ConfigEncryptionHelper.EncryptConfig(plaintext, _key, ConfigFileType.MainConfig, _argonSalt);

        // Flip first salt byte (index 6) — salt is part of AAD so any change invalidates GCM tag
        encrypted[6] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() =>
            ConfigEncryptionHelper.DecryptConfig(encrypted, _key, ConfigFileType.MainConfig));
    }
}