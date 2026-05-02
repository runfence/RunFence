using System.Security.Cryptography;
using Moq;
using RunFence.Core;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class RememberPinServiceTests : IDisposable
{
    private const string LegacyTpmKeyName = "RunFence-AutostartKey";
    private const byte DpapiOnlyVersion = 0x01;
    private const byte TpmHybridVersion = 0x02;
    private const byte DpapiMode = 0x00;
    private const byte TpmMode = 0x01;
    private const int WrappedKeyLengthOffset = 2;
    private const int WrappedKeyDataOffset = 4;
    private const int TpmWrappedDataKeyLength = 32;

    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IMachineIdProvider> _machineIdProvider = new();
    private readonly Mock<IConfigPaths> _configPaths = new();
    private readonly Mock<ITpmKeyProvider> _tpmKeyProvider = new();
    private readonly TempDirectory _tempDir = new("RememberPinService");
    private readonly string _startKeyPath;
    private readonly RememberPinService _service;

    public RememberPinServiceTests()
    {
        _startKeyPath = Path.Combine(_tempDir.Path, "startkey.dat");
        _configPaths.Setup(p => p.RememberPinFilePath).Returns(_startKeyPath);
        _machineIdProvider.Setup(m => m.MachineIdHash).Returns([0x6B, 0x29, 0xFC, 0x40, 0xCA, 0x47, 0x10, 0x67, 0xB3, 0x1D, 0x00, 0xDD]);
        _service = new RememberPinService(_log.Object, _machineIdProvider.Object, _configPaths.Object, _tpmKeyProvider.Object);
    }

    public void Dispose() => _tempDir.Dispose();

    private static byte[] MakePinDerivedKey()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);
        return key;
    }

    private static byte[] GetWrappedKey(byte[] fileContent)
    {
        ushort wrappedKeyLength = BitConverter.ToUInt16(fileContent, WrappedKeyLengthOffset);
        return fileContent.AsSpan(WrappedKeyDataOffset, wrappedKeyLength).ToArray();
    }

    [Fact]
    public void IsEnabled_ReturnsFalse_WhenFileDoesNotExist()
    {
        Assert.False(_service.IsEnabled);
    }

    [Fact]
    public void IsEnabled_ReturnsTrue_WhenFileExists()
    {
        File.WriteAllBytes(_startKeyPath, [DpapiOnlyVersion, DpapiMode]);
        Assert.True(_service.IsEnabled);
    }

    [Fact]
    public void IsTpmAvailable_DelegatesToTpmKeyProvider()
    {
        _tpmKeyProvider.Setup(t => t.IsAvailable()).Returns(true);

        Assert.True(_service.IsTpmAvailable());

        _tpmKeyProvider.Verify(t => t.IsAvailable(), Times.Once);
    }

    [Fact]
    public void EnableWithTpm_WritesHybridEnvelope_AndWrapsFixedSizeDataKey()
    {
        var pinDerivedKey = MakePinDerivedKey();
        using var protectedPinDerivedKey = new ProtectedBuffer(pinDerivedKey, protect: false);
        var fakeWrappedKey = new byte[] { 0xAA, 0xBB, 0xCC };
        byte[]? wrappedKeyInput = null;
        _tpmKeyProvider.Setup(t => t.Encrypt(LegacyTpmKeyName, It.IsAny<byte[]>()))
            .Callback<string, byte[]>((_, key) => wrappedKeyInput = key.ToArray())
            .Returns(fakeWrappedKey.ToArray());

        _service.EnableWithTpm(protectedPinDerivedKey);

        _tpmKeyProvider.Verify(t => t.CreateKey(LegacyTpmKeyName, 2048), Times.Once);
        _tpmKeyProvider.Verify(t => t.Encrypt(LegacyTpmKeyName, It.IsAny<byte[]>()), Times.Once);
        Assert.NotNull(wrappedKeyInput);
        Assert.Equal(TpmWrappedDataKeyLength, wrappedKeyInput!.Length);

        var content = File.ReadAllBytes(_startKeyPath);
        Assert.True(content.Length > WrappedKeyDataOffset + fakeWrappedKey.Length);
        Assert.Equal(TpmHybridVersion, content[0]);
        Assert.Equal(TpmMode, content[1]);
        Assert.Equal(fakeWrappedKey, GetWrappedKey(content));
    }

    [Fact]
    public void EnableDpapiOnly_WritesFileWithModeByte0_AndNoTpmCalls()
    {
        var pinDerivedKey = MakePinDerivedKey();
        using var protectedPinDerivedKey = new ProtectedBuffer(pinDerivedKey, protect: false);

        _service.EnableDpapiOnly(protectedPinDerivedKey);

        _tpmKeyProvider.Verify(t => t.CreateKey(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _tpmKeyProvider.Verify(t => t.Encrypt(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);

        var content = File.ReadAllBytes(_startKeyPath);
        Assert.True(content.Length > 2);
        Assert.Equal(DpapiOnlyVersion, content[0]);
        Assert.Equal(DpapiMode, content[1]);
    }

    [Fact]
    public void TryDecrypt_DpapiMode_ReturnsCorrectKey_WithNoTpmCalls()
    {
        var pinDerivedKey = MakePinDerivedKey();
        using var protectedPinDerivedKey = new ProtectedBuffer(pinDerivedKey, protect: false);
        _service.EnableDpapiOnly(protectedPinDerivedKey);

        var result = _service.TryDecrypt(out var decryptedKey);

        Assert.True(result);
        Assert.Equal(pinDerivedKey, decryptedKey);
        _tpmKeyProvider.Verify(t => t.Decrypt(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
        CryptographicOperations.ZeroMemory(decryptedKey);
    }

    [Fact]
    public void TryDecrypt_TpmMode_CallsTpmDecryptAndReturnsCorrectKey()
    {
        var pinDerivedKey = MakePinDerivedKey();
        using var protectedPinDerivedKey = new ProtectedBuffer(pinDerivedKey, protect: false);

        byte[]? capturedDataKey = null;
        _tpmKeyProvider.Setup(t => t.Encrypt(LegacyTpmKeyName, It.IsAny<byte[]>()))
            .Callback<string, byte[]>((_, key) => capturedDataKey = key.ToArray())
            .Returns<string, byte[]>((_, key) => key.ToArray());
        _tpmKeyProvider.Setup(t => t.Decrypt(LegacyTpmKeyName, It.IsAny<byte[]>()))
            .Returns<string, byte[]>((_, _) => capturedDataKey!.ToArray());
        _tpmKeyProvider.Setup(t => t.DecryptExact(LegacyTpmKeyName, It.IsAny<byte[]>(), TpmWrappedDataKeyLength))
            .Returns<string, byte[], int>((_, _, _) => capturedDataKey!.ToArray());

        _service.EnableWithTpm(protectedPinDerivedKey);

        var result = _service.TryDecrypt(out var decryptedKey);

        Assert.True(result);
        Assert.Equal(pinDerivedKey, decryptedKey);
        _tpmKeyProvider.Verify(t => t.DecryptExact(LegacyTpmKeyName, It.IsAny<byte[]>(), TpmWrappedDataKeyLength), Times.Once);
        _tpmKeyProvider.Verify(t => t.Decrypt(LegacyTpmKeyName, It.IsAny<byte[]>()), Times.Never);
        CryptographicOperations.ZeroMemory(decryptedKey);
    }

    [Fact]
    public void TryDecrypt_TpmMode_LegacyVersion1Payload_RemainsCompatible()
    {
        var pinDerivedKey = MakePinDerivedKey();
        using var protectedPinDerivedKey = new ProtectedBuffer(pinDerivedKey, protect: false);

        _service.EnableDpapiOnly(protectedPinDerivedKey);
        var dpapiContent = File.ReadAllBytes(_startKeyPath);
        var dpapiBlob = dpapiContent[2..].ToArray();
        File.WriteAllBytes(_startKeyPath, [DpapiOnlyVersion, TpmMode, .. dpapiBlob]);

        _tpmKeyProvider.Setup(t => t.Decrypt(LegacyTpmKeyName, It.IsAny<byte[]>()))
            .Returns(dpapiBlob.ToArray());

        var result = _service.TryDecrypt(out var decryptedKey);

        Assert.True(result);
        Assert.Equal(pinDerivedKey, decryptedKey);
        CryptographicOperations.ZeroMemory(decryptedKey);
        CryptographicOperations.ZeroMemory(dpapiBlob);
    }

    [Fact]
    public void TryDecrypt_TpmPcrMismatch_ReturnsFalse()
    {
        var pinDerivedKey = MakePinDerivedKey();
        using var protectedPinDerivedKey = new ProtectedBuffer(pinDerivedKey, protect: false);
        _tpmKeyProvider.Setup(t => t.Encrypt(It.IsAny<string>(), It.IsAny<byte[]>())).Returns(new byte[] { 0xAA, 0xBB });
        _tpmKeyProvider.Setup(t => t.Decrypt(LegacyTpmKeyName, It.IsAny<byte[]>()))
            .Throws(new CryptographicException("TPM PCR mismatch"));
        _tpmKeyProvider.Setup(t => t.DecryptExact(LegacyTpmKeyName, It.IsAny<byte[]>(), TpmWrappedDataKeyLength))
            .Throws(new CryptographicException("TPM PCR mismatch"));

        _service.EnableWithTpm(protectedPinDerivedKey);

        var result = _service.TryDecrypt(out var decryptedKey);

        Assert.False(result);
        Assert.Empty(decryptedKey);
    }

    [Fact]
    public void TryDecrypt_MissingFile_ReturnsFalse()
    {
        var result = _service.TryDecrypt(out var key);

        Assert.False(result);
        Assert.Empty(key);
    }

    [Fact]
    public void TryDecrypt_CorruptFile_TooShort_ReturnsFalse()
    {
        File.WriteAllBytes(_startKeyPath, [DpapiOnlyVersion]);

        var result = _service.TryDecrypt(out var key);

        Assert.False(result);
        Assert.Empty(key);
    }

    [Fact]
    public void TryDecrypt_UnknownVersion_ReturnsFalse()
    {
        File.WriteAllBytes(_startKeyPath, [0x03, DpapiMode, 0x01, 0x02, 0x03]);

        var result = _service.TryDecrypt(out var key);

        Assert.False(result);
        Assert.Empty(key);
    }

    [Fact]
    public void Disable_TpmMode_DeletesFileAndCallsTpmDeleteKey()
    {
        var pinDerivedKey = MakePinDerivedKey();
        using var protectedPinDerivedKey = new ProtectedBuffer(pinDerivedKey, protect: false);
        _tpmKeyProvider.Setup(t => t.Encrypt(LegacyTpmKeyName, It.IsAny<byte[]>()))
            .Returns(new byte[] { 0xAA, 0xBB, 0xCC });
        _service.EnableWithTpm(protectedPinDerivedKey);
        Assert.True(File.Exists(_startKeyPath));

        _service.Disable();

        Assert.False(File.Exists(_startKeyPath));
        _tpmKeyProvider.Verify(t => t.DeleteKey(LegacyTpmKeyName), Times.Once);
    }

    [Fact]
    public void Disable_TpmMode_WhenDeleteKeyThrows_StillDeletesFile()
    {
        var pinDerivedKey = MakePinDerivedKey();
        using var protectedPinDerivedKey = new ProtectedBuffer(pinDerivedKey, protect: false);
        _tpmKeyProvider.Setup(t => t.Encrypt(LegacyTpmKeyName, It.IsAny<byte[]>()))
            .Returns(new byte[] { 0xAA, 0xBB, 0xCC });
        _tpmKeyProvider.Setup(t => t.DeleteKey(LegacyTpmKeyName))
            .Throws(new CryptographicException("TPM unavailable"));
        _service.EnableWithTpm(protectedPinDerivedKey);
        Assert.True(File.Exists(_startKeyPath));

        _service.Disable();

        Assert.False(File.Exists(_startKeyPath));
        _tpmKeyProvider.Verify(t => t.DeleteKey(LegacyTpmKeyName), Times.Once);
    }

    [Fact]
    public void Disable_DpapiMode_DeletesFileWithoutCallingTpmDelete()
    {
        var pinDerivedKey = MakePinDerivedKey();
        using var protectedPinDerivedKey = new ProtectedBuffer(pinDerivedKey, protect: false);
        _service.EnableDpapiOnly(protectedPinDerivedKey);
        Assert.True(File.Exists(_startKeyPath));

        _service.Disable();

        Assert.False(File.Exists(_startKeyPath));
        _tpmKeyProvider.Verify(t => t.DeleteKey(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Disable_WhenFileNotExist_DoesNotThrowAndDoesNotCallTpmDelete()
    {
        _service.Disable();

        _tpmKeyProvider.Verify(t => t.DeleteKey(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void UpdateForPinChange_WhenNotEnabled_IsNoOp()
    {
        var newKey = MakePinDerivedKey();
        using var protectedNewKey = new ProtectedBuffer(newKey, protect: false);

        _service.UpdateForPinChange(protectedNewKey);

        _tpmKeyProvider.Verify(t => t.CreateKey(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _tpmKeyProvider.Verify(t => t.Encrypt(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
        Assert.False(File.Exists(_startKeyPath));
    }

    [Fact]
    public void UpdateForPinChange_DpapiMode_PreservesDpapiMode()
    {
        var oldKey = MakePinDerivedKey();
        using var protectedOldKey = new ProtectedBuffer(oldKey, protect: false);
        _service.EnableDpapiOnly(protectedOldKey);

        var newKey = new byte[32];
        new Random(99).NextBytes(newKey);
        using var protectedNewKey = new ProtectedBuffer(newKey, protect: false);

        _service.UpdateForPinChange(protectedNewKey);

        var content = File.ReadAllBytes(_startKeyPath);
        Assert.Equal(DpapiOnlyVersion, content[0]);
        Assert.Equal(DpapiMode, content[1]);
        _tpmKeyProvider.Verify(t => t.CreateKey(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void UpdateForPinChange_TpmMode_PreservesTpmMode()
    {
        var oldKey = MakePinDerivedKey();
        var newKey = new byte[32];
        new Random(99).NextBytes(newKey);
        using var protectedOldKey = new ProtectedBuffer(oldKey, protect: false);
        using var protectedNewKey = new ProtectedBuffer(newKey, protect: false);

        var fakeWrappedKey1 = new byte[] { 0xAA, 0xBB };
        var fakeWrappedKey2 = new byte[] { 0xCC, 0xDD };
        _tpmKeyProvider.SetupSequence(t => t.Encrypt(LegacyTpmKeyName, It.IsAny<byte[]>()))
            .Returns(fakeWrappedKey1.ToArray())
            .Returns(fakeWrappedKey2.ToArray());

        _service.EnableWithTpm(protectedOldKey);

        _service.UpdateForPinChange(protectedNewKey);

        var content = File.ReadAllBytes(_startKeyPath);
        Assert.Equal(TpmHybridVersion, content[0]);
        Assert.Equal(TpmMode, content[1]);
        Assert.Equal(fakeWrappedKey2, GetWrappedKey(content));
        _tpmKeyProvider.Verify(t => t.CreateKey(LegacyTpmKeyName, 2048), Times.Exactly(2));
    }

    [Fact]
    public void UpdateForPinChange_UnknownVersion_IsNoOp()
    {
        File.WriteAllBytes(_startKeyPath, [0x03, DpapiMode, 0x01, 0x02, 0x03]);

        var newKey = MakePinDerivedKey();
        using var protectedNewKey = new ProtectedBuffer(newKey, protect: false);

        _service.UpdateForPinChange(protectedNewKey);

        _tpmKeyProvider.Verify(t => t.CreateKey(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _tpmKeyProvider.Verify(t => t.Encrypt(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
        var content = File.ReadAllBytes(_startKeyPath);
        Assert.Equal(0x03, content[0]);
    }

    [Fact]
    public void UpdateForPinChange_TpmMode_FallsBackToDpapi_WhenTpmThrows()
    {
        var oldKey = MakePinDerivedKey();
        var newKey = new byte[32];
        new Random(99).NextBytes(newKey);
        using var protectedOldKey = new ProtectedBuffer(oldKey, protect: false);
        using var protectedNewKey = new ProtectedBuffer(newKey, protect: false);

        _tpmKeyProvider.Setup(t => t.Encrypt(LegacyTpmKeyName, It.IsAny<byte[]>()))
            .Returns(new byte[] { 0xAA, 0xBB });
        _service.EnableWithTpm(protectedOldKey);

        _tpmKeyProvider.Setup(t => t.CreateKey(LegacyTpmKeyName, 2048))
            .Throws(new CryptographicException("TPM unavailable"));

        _service.UpdateForPinChange(protectedNewKey);

        var content = File.ReadAllBytes(_startKeyPath);
        Assert.Equal(DpapiOnlyVersion, content[0]);
        Assert.Equal(DpapiMode, content[1]);
    }
}
