using System.Security.Cryptography;
using Moq;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class TpmKeyProviderTests
{
    private readonly Mock<ITpmNativeApi> _api = new(MockBehavior.Strict);

    private static readonly IntPtr FakeProvider = new(0x1000);
    private static readonly IntPtr FakeKey = new(0x2000);

    private TpmHandleProvider BuildHandleProvider() => new(_api.Object);
    private TpmPublicKeyExporter BuildPublicKeyExporter() => new(_api.Object);
    private TpmKeyCreationPolicy BuildKeyCreationPolicy() => new(_api.Object, BuildHandleProvider(), Mock.Of<RunFence.Core.ILoggingService>());
    private TpmDecryptPolicy BuildDecryptPolicy() => new(_api.Object, BuildHandleProvider());

    private TpmKeyProvider BuildProvider(
        TpmHandleProvider? handleProvider = null,
        TpmPublicKeyExporter? exporter = null,
        TpmKeyCreationPolicy? creationPolicy = null,
        TpmDecryptPolicy? decryptPolicy = null)
    {
        handleProvider ??= BuildHandleProvider();
        exporter ??= BuildPublicKeyExporter();
        creationPolicy ??= BuildKeyCreationPolicy();
        decryptPolicy ??= BuildDecryptPolicy();
        return new TpmKeyProvider(_api.Object, handleProvider, exporter, creationPolicy, decryptPolicy);
    }

    /// <summary>
    /// Sets up the mock native API to simulate a successful two-phase NCryptDecrypt call
    /// (size query followed by data query) returning the specified plaintext bytes with the
    /// given actual result count.
    /// </summary>
    private void SetupSuccessfulDecrypt(byte[] plaintext, int actualResult)
    {
        _api.Setup(a => a.NCryptOpenStorageProvider(out It.Ref<IntPtr>.IsAny, TpmNative.MS_PLATFORM_CRYPTO_PROVIDER, 0))
            .Callback(new OpenStorageProviderCallback((out IntPtr h, string _, int __) => h = FakeProvider))
            .Returns(0);

        _api.Setup(a => a.NCryptOpenKey(FakeProvider, out It.Ref<IntPtr>.IsAny, "MyKey", 0, 0))
            .Callback(new OpenKeyCallback((IntPtr _, out IntPtr h, string __, int ___, int ____) => h = FakeKey))
            .Returns(0);

        _api.Setup(a => a.NCryptDecrypt(FakeKey, It.IsAny<byte[]>(), It.IsAny<int>(),
                ref It.Ref<OaepPaddingInfo>.IsAny, null, 0, out It.Ref<int>.IsAny, TpmNative.BCRYPT_PAD_OAEP))
            .Callback(new DecryptCallback((IntPtr _, byte[] __, int ___, ref OaepPaddingInfo ____, byte[]? _____, int ______, out int cb, int _______) => cb = plaintext.Length))
            .Returns(0);

        _api.Setup(a => a.NCryptDecrypt(FakeKey, It.IsAny<byte[]>(), It.IsAny<int>(),
                ref It.Ref<OaepPaddingInfo>.IsAny, It.Is<byte[]>(b => b != null), It.IsAny<int>(), out It.Ref<int>.IsAny, TpmNative.BCRYPT_PAD_OAEP))
            .Callback(new DecryptCallback((IntPtr _, byte[] __, int ___, ref OaepPaddingInfo ____, byte[]? output, int ______, out int cb, int _______) =>
            {
                if (output != null)
                    Buffer.BlockCopy(plaintext, 0, output, 0, plaintext.Length);
                cb = actualResult;
            }))
            .Returns(0);

        _api.Setup(a => a.NCryptFreeObject(FakeKey)).Returns(0);
        _api.Setup(a => a.NCryptFreeObject(FakeProvider)).Returns(0);
    }

    // ── IsAvailable ──────────────────────────────────────────────────────────

    [Fact]
    public void IsAvailable_WhenProviderOpensSuccessfully_ReturnsTrueAndFreesHandle()
    {
        // Arrange
        _api.Setup(a => a.NCryptOpenStorageProvider(out It.Ref<IntPtr>.IsAny, TpmNative.MS_PLATFORM_CRYPTO_PROVIDER, 0))
            .Callback(new OpenStorageProviderCallback((out IntPtr h, string _, int __) => h = FakeProvider))
            .Returns(0);
        _api.Setup(a => a.NCryptFreeObject(FakeProvider)).Returns(0);

        var provider = BuildProvider();

        // Act
        bool result = provider.IsAvailable();

        // Assert
        Assert.True(result);
        _api.Verify(a => a.NCryptFreeObject(FakeProvider), Times.Once);
    }

    [Fact]
    public void IsAvailable_WhenProviderOpenFails_ReturnsFalseAndFreesNoHandle()
    {
        // Arrange
        _api.Setup(a => a.NCryptOpenStorageProvider(out It.Ref<IntPtr>.IsAny, TpmNative.MS_PLATFORM_CRYPTO_PROVIDER, 0))
            .Callback(new OpenStorageProviderCallback((out IntPtr h, string _, int __) => h = IntPtr.Zero))
            .Returns(TpmNative.NTE_NOT_FOUND);

        var provider = BuildProvider();

        // Act
        bool result = provider.IsAvailable();

        // Assert
        Assert.False(result);
        _api.Verify(a => a.NCryptFreeObject(It.IsAny<IntPtr>()), Times.Never);
    }

    [Fact]
    public void IsAvailable_CachesResult_DoesNotCallNativeAgainOnSecondCall()
    {
        // Arrange
        _api.Setup(a => a.NCryptOpenStorageProvider(out It.Ref<IntPtr>.IsAny, TpmNative.MS_PLATFORM_CRYPTO_PROVIDER, 0))
            .Callback(new OpenStorageProviderCallback((out IntPtr h, string _, int __) => h = FakeProvider))
            .Returns(0);
        _api.Setup(a => a.NCryptFreeObject(FakeProvider)).Returns(0);

        var provider = BuildProvider();
        provider.IsAvailable();

        // Act
        bool second = provider.IsAvailable();

        // Assert
        Assert.True(second);
        _api.Verify(a => a.NCryptOpenStorageProvider(out It.Ref<IntPtr>.IsAny, It.IsAny<string>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void IsAvailable_WhenNativeThrows_ReturnsFalseAndCachesFailure()
    {
        // Arrange
        _api.Setup(a => a.NCryptOpenStorageProvider(out It.Ref<IntPtr>.IsAny, TpmNative.MS_PLATFORM_CRYPTO_PROVIDER, 0))
            .Throws(new InvalidOperationException("DLL not found"));

        var provider = BuildProvider();

        // Act
        bool first = provider.IsAvailable();
        bool second = provider.IsAvailable();

        // Assert
        Assert.False(first);
        Assert.False(second);
        _api.Verify(a => a.NCryptOpenStorageProvider(out It.Ref<IntPtr>.IsAny, It.IsAny<string>(), It.IsAny<int>()), Times.Once);
    }

    // ── Key creation ─────────────────────────────────────────────────────────

    [Fact]
    public void CreateKey_DeletesExistingKey_ThenCreatesWithPcrBinding()
    {
        // Arrange
        _api.Setup(a => a.NCryptOpenStorageProvider(out It.Ref<IntPtr>.IsAny, TpmNative.MS_PLATFORM_CRYPTO_PROVIDER, 0))
            .Callback(new OpenStorageProviderCallback((out IntPtr h, string _, int __) => h = FakeProvider))
            .Returns(0);

        _api.Setup(a => a.NCryptOpenKey(FakeProvider, out It.Ref<IntPtr>.IsAny, "MyKey", 0, 0))
            .Callback(new OpenKeyCallback((IntPtr _, out IntPtr h, string __, int ___, int ____) => h = IntPtr.Zero))
            .Returns(TpmNative.NTE_NOT_FOUND);

        _api.Setup(a => a.NCryptCreatePersistedKey(FakeProvider, out It.Ref<IntPtr>.IsAny, TpmNative.BCRYPT_RSA_ALGORITHM, "MyKey", 0, 0))
            .Callback(new CreatePersistedKeyCallback((IntPtr _, out IntPtr h, string __, string? ___, int ____, int _____) => h = FakeKey))
            .Returns(0);

        int keySizeArg = 2048;
        _api.Setup(a => a.NCryptSetPropertyInt(FakeKey, TpmNative.NCRYPT_LENGTH_PROPERTY, ref keySizeArg, 4, 0))
            .Returns(0);

        _api.Setup(a => a.NCryptSetPropertyBytes(FakeKey, TpmNative.NCRYPT_PCP_PLATFORM_BINDING_PCRMASK_PROPERTY, It.IsAny<byte[]>(), 3, 0))
            .Returns(0);

        _api.Setup(a => a.NCryptFinalizeKey(FakeKey, TpmNative.NCRYPT_SILENT_FLAG)).Returns(0);
        _api.Setup(a => a.NCryptFreeObject(FakeProvider)).Returns(0);
        _api.Setup(a => a.NCryptFreeObject(FakeKey)).Returns(0);

        var policy = BuildKeyCreationPolicy();

        // Act
        policy.CreateKey("MyKey", 2048);

        // Assert
        _api.Verify(a => a.NCryptSetPropertyBytes(FakeKey, TpmNative.NCRYPT_PCP_PLATFORM_BINDING_PCRMASK_PROPERTY, It.IsAny<byte[]>(), 3, 0), Times.Once);
        _api.Verify(a => a.NCryptFinalizeKey(FakeKey, TpmNative.NCRYPT_SILENT_FLAG), Times.Once);
    }

    [Fact]
    public void CreateKey_WhenPcrBindingFails_DeletesPartialKeyAndRetriesWithoutPcr()
    {
        // Arrange
        _api.Setup(a => a.NCryptOpenStorageProvider(out It.Ref<IntPtr>.IsAny, TpmNative.MS_PLATFORM_CRYPTO_PROVIDER, 0))
            .Callback(new OpenStorageProviderCallback((out IntPtr h, string _, int __) => h = FakeProvider))
            .Returns(0);

        _api.Setup(a => a.NCryptOpenKey(FakeProvider, out It.Ref<IntPtr>.IsAny, "MyKey", 0, 0))
            .Callback(new OpenKeyCallback((IntPtr _, out IntPtr h, string __, int ___, int ____) => h = IntPtr.Zero))
            .Returns(TpmNative.NTE_NOT_FOUND);

        _api.Setup(a => a.NCryptCreatePersistedKey(FakeProvider, out It.Ref<IntPtr>.IsAny, TpmNative.BCRYPT_RSA_ALGORITHM, "MyKey", 0, 0))
            .Callback(new CreatePersistedKeyCallback((IntPtr _, out IntPtr h, string __, string? ___, int ____, int _____) => h = FakeKey))
            .Returns(0);

        int keySizeArg = 2048;
        _api.Setup(a => a.NCryptSetPropertyInt(FakeKey, TpmNative.NCRYPT_LENGTH_PROPERTY, ref keySizeArg, 4, 0))
            .Returns(0);

        _api.Setup(a => a.NCryptSetPropertyBytes(FakeKey, TpmNative.NCRYPT_PCP_PLATFORM_BINDING_PCRMASK_PROPERTY, It.IsAny<byte[]>(), 3, 0))
            .Returns(unchecked((int)0x80090009));

        _api.Setup(a => a.NCryptDeleteKey(FakeKey, 0)).Returns(0);
        _api.Setup(a => a.NCryptFinalizeKey(FakeKey, TpmNative.NCRYPT_SILENT_FLAG)).Returns(0);
        _api.Setup(a => a.NCryptFreeObject(FakeProvider)).Returns(0);
        _api.Setup(a => a.NCryptFreeObject(FakeKey)).Returns(0);
        _api.Setup(a => a.NCryptFreeObject(IntPtr.Zero)).Returns(0);

        var mockLog = new Mock<RunFence.Core.ILoggingService>();
        var handleProvider = BuildHandleProvider();
        var policy = new TpmKeyCreationPolicy(_api.Object, handleProvider, mockLog.Object);

        // Act
        policy.CreateKey("MyKey", 2048);

        // Assert
        _api.Verify(a => a.NCryptSetPropertyBytes(FakeKey, TpmNative.NCRYPT_PCP_PLATFORM_BINDING_PCRMASK_PROPERTY, It.IsAny<byte[]>(), 3, 0), Times.Once);
        _api.Verify(a => a.NCryptDeleteKey(FakeKey, 0), Times.Once);
        _api.Verify(a => a.NCryptFinalizeKey(FakeKey, TpmNative.NCRYPT_SILENT_FLAG), Times.Once);
        mockLog.Verify(l => l.Warn(It.Is<string>(s => s.Contains("PCR binding failed"))), Times.Once);
    }

    [Fact]
    public void CreateKey_WhenFinalizeFailsAfterPcr_CleansUpKey()
    {
        // Arrange
        _api.Setup(a => a.NCryptOpenStorageProvider(out It.Ref<IntPtr>.IsAny, TpmNative.MS_PLATFORM_CRYPTO_PROVIDER, 0))
            .Callback(new OpenStorageProviderCallback((out IntPtr h, string _, int __) => h = FakeProvider))
            .Returns(0);

        _api.Setup(a => a.NCryptOpenKey(FakeProvider, out It.Ref<IntPtr>.IsAny, "MyKey", 0, 0))
            .Callback(new OpenKeyCallback((IntPtr _, out IntPtr h, string __, int ___, int ____) => h = IntPtr.Zero))
            .Returns(TpmNative.NTE_NOT_FOUND);

        _api.Setup(a => a.NCryptCreatePersistedKey(FakeProvider, out It.Ref<IntPtr>.IsAny, TpmNative.BCRYPT_RSA_ALGORITHM, "MyKey", 0, 0))
            .Callback(new CreatePersistedKeyCallback((IntPtr _, out IntPtr h, string __, string? ___, int ____, int _____) => h = FakeKey))
            .Returns(0);

        int keySizeArg = 2048;
        _api.Setup(a => a.NCryptSetPropertyInt(FakeKey, TpmNative.NCRYPT_LENGTH_PROPERTY, ref keySizeArg, 4, 0))
            .Returns(0);

        _api.Setup(a => a.NCryptSetPropertyBytes(FakeKey, TpmNative.NCRYPT_PCP_PLATFORM_BINDING_PCRMASK_PROPERTY, It.IsAny<byte[]>(), 3, 0))
            .Returns(0);

        _api.Setup(a => a.NCryptFinalizeKey(FakeKey, TpmNative.NCRYPT_SILENT_FLAG))
            .Returns(unchecked((int)0x80090009));

        _api.Setup(a => a.NCryptDeleteKey(FakeKey, 0)).Returns(0);
        _api.Setup(a => a.NCryptFreeObject(FakeProvider)).Returns(0);
        _api.Setup(a => a.NCryptFreeObject(It.Is<IntPtr>(p => p == FakeKey || p == IntPtr.Zero))).Returns(0);

        var policy = BuildKeyCreationPolicy();

        // Act & Assert
        Assert.Throws<CryptographicException>(() => policy.CreateKey("MyKey", 2048));
        _api.Verify(a => a.NCryptDeleteKey(FakeKey, 0), Times.Once);
    }

    // ── Public key export ─────────────────────────────────────────────────────

    [Fact]
    public void ExportPublicKey_ParsesBcryptRsaPublicBlob_IntoRsaParameters()
    {
        // Arrange: build a minimal BCRYPT_RSAKEY_BLOB for 2048-bit RSA
        var exponent = new byte[] { 0x01, 0x00, 0x01 };
        var modulus = new byte[256];
        modulus[0] = 0xC1;
        modulus[255] = 0x01;

        var blob = new byte[24 + exponent.Length + modulus.Length];
        BitConverter.TryWriteBytes(blob.AsSpan(0), 0x31415352);
        BitConverter.TryWriteBytes(blob.AsSpan(4), 2048);
        BitConverter.TryWriteBytes(blob.AsSpan(8), exponent.Length);
        BitConverter.TryWriteBytes(blob.AsSpan(12), modulus.Length);
        Buffer.BlockCopy(exponent, 0, blob, 24, exponent.Length);
        Buffer.BlockCopy(modulus, 0, blob, 24 + exponent.Length, modulus.Length);

        _api.Setup(a => a.NCryptExportKey(FakeKey, IntPtr.Zero, TpmNative.BCRYPT_RSAPUBLIC_BLOB, IntPtr.Zero, null, 0, out It.Ref<int>.IsAny, 0))
            .Callback(new ExportKeySizeCallback((IntPtr _, IntPtr __, string ___, IntPtr ____, byte[]? _____, int ______, out int cb, int _______) => cb = blob.Length))
            .Returns(0);

        _api.Setup(a => a.NCryptExportKey(FakeKey, IntPtr.Zero, TpmNative.BCRYPT_RSAPUBLIC_BLOB, IntPtr.Zero, It.Is<byte[]>(b => b != null && b.Length == blob.Length), blob.Length, out It.Ref<int>.IsAny, 0))
            .Callback(new ExportKeyDataCallback((IntPtr _, IntPtr __, string ___, IntPtr ____, byte[]? output, int ______, out int cb, int _______) =>
            {
                if (output != null)
                    Buffer.BlockCopy(blob, 0, output, 0, blob.Length);
                cb = blob.Length;
            }))
            .Returns(0);

        var exporter = BuildPublicKeyExporter();

        // Act
        using var rsa = exporter.ExportPublicKey(FakeKey);

        // Assert
        var parameters = rsa.ExportParameters(includePrivateParameters: false);
        Assert.Equal(exponent, parameters.Exponent);
        Assert.Equal(modulus, parameters.Modulus);
        _api.Verify(a => a.NCryptDecrypt(It.IsAny<IntPtr>(), It.IsAny<byte[]>(), It.IsAny<int>(),
            ref It.Ref<OaepPaddingInfo>.IsAny, It.IsAny<byte[]>(), It.IsAny<int>(), out It.Ref<int>.IsAny, It.IsAny<int>()), Times.Never);
    }

    // ── Decrypt PCR mismatch ──────────────────────────────────────────────────

    [Fact]
    public void DecryptCore_WhenNteBADKEYSTATE_DeletesKeyAndThrowsPcrMismatch()
    {
        // Arrange
        _api.Setup(a => a.NCryptOpenStorageProvider(out It.Ref<IntPtr>.IsAny, TpmNative.MS_PLATFORM_CRYPTO_PROVIDER, 0))
            .Callback(new OpenStorageProviderCallback((out IntPtr h, string _, int __) => h = FakeProvider))
            .Returns(0);

        _api.Setup(a => a.NCryptOpenKey(FakeProvider, out It.Ref<IntPtr>.IsAny, "MyKey", 0, 0))
            .Callback(new OpenKeyCallback((IntPtr _, out IntPtr h, string __, int ___, int ____) => h = FakeKey))
            .Returns(0);

        _api.Setup(a => a.NCryptDecrypt(FakeKey, It.IsAny<byte[]>(), It.IsAny<int>(),
                ref It.Ref<OaepPaddingInfo>.IsAny, null, 0, out It.Ref<int>.IsAny, TpmNative.BCRYPT_PAD_OAEP))
            .Returns(TpmNative.NTE_BAD_KEY_STATE);

        _api.Setup(a => a.NCryptDeleteKey(FakeKey, 0)).Returns(0);
        _api.Setup(a => a.NCryptFreeObject(FakeProvider)).Returns(0);
        _api.Setup(a => a.NCryptFreeObject(IntPtr.Zero)).Returns(0);

        var decryptPolicy = BuildDecryptPolicy();

        // Act & Assert
        var ex = Assert.Throws<CryptographicException>(() => decryptPolicy.DecryptCore("MyKey", new byte[256], null));
        Assert.Equal("TPM PCR mismatch", ex.Message);
        _api.Verify(a => a.NCryptDeleteKey(FakeKey, 0), Times.Once);
    }

    [Fact]
    public void DecryptCore_WhenNteBADKEYSTATEOnSecondDecrypt_DeletesKeyAndThrowsPcrMismatch()
    {
        // Arrange
        _api.Setup(a => a.NCryptOpenStorageProvider(out It.Ref<IntPtr>.IsAny, TpmNative.MS_PLATFORM_CRYPTO_PROVIDER, 0))
            .Callback(new OpenStorageProviderCallback((out IntPtr h, string _, int __) => h = FakeProvider))
            .Returns(0);

        _api.Setup(a => a.NCryptOpenKey(FakeProvider, out It.Ref<IntPtr>.IsAny, "MyKey", 0, 0))
            .Callback(new OpenKeyCallback((IntPtr _, out IntPtr h, string __, int ___, int ____) => h = FakeKey))
            .Returns(0);

        _api.Setup(a => a.NCryptDecrypt(FakeKey, It.IsAny<byte[]>(), It.IsAny<int>(),
                ref It.Ref<OaepPaddingInfo>.IsAny, null, 0, out It.Ref<int>.IsAny, TpmNative.BCRYPT_PAD_OAEP))
            .Callback(new DecryptCallback((IntPtr _, byte[] __, int ___, ref OaepPaddingInfo ____, byte[]? _____, int ______, out int cb, int _______) => cb = 32))
            .Returns(0);

        _api.Setup(a => a.NCryptDecrypt(FakeKey, It.IsAny<byte[]>(), It.IsAny<int>(),
                ref It.Ref<OaepPaddingInfo>.IsAny, It.Is<byte[]>(b => b != null), It.IsAny<int>(), out It.Ref<int>.IsAny, TpmNative.BCRYPT_PAD_OAEP))
            .Returns(TpmNative.NTE_BAD_KEY_STATE);

        _api.Setup(a => a.NCryptDeleteKey(FakeKey, 0)).Returns(0);
        _api.Setup(a => a.NCryptFreeObject(FakeProvider)).Returns(0);
        _api.Setup(a => a.NCryptFreeObject(IntPtr.Zero)).Returns(0);

        var decryptPolicy = BuildDecryptPolicy();

        // Act & Assert
        var ex = Assert.Throws<CryptographicException>(() => decryptPolicy.DecryptCore("MyKey", new byte[256], null));
        Assert.Equal("TPM PCR mismatch", ex.Message);
        _api.Verify(a => a.NCryptDeleteKey(FakeKey, 0), Times.Once);
    }

    // ── DecryptExact (fixed-length output normalization) ──────────────────────

    [Fact]
    public void DecryptExact_ExactLengthOutput_ReturnsExpectedBytes()
    {
        // Arrange: TPM returns exactly 4 bytes, expected length is 4
        var plaintext = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        SetupSuccessfulDecrypt(plaintext, actualResult: 4);

        var decryptPolicy = BuildDecryptPolicy();

        // Act
        var result = decryptPolicy.DecryptCore("MyKey", new byte[256], expectedLength: 4);

        // Assert
        Assert.Equal(plaintext, result);
    }

    [Fact]
    public void DecryptExact_WithZeroPaddingAfterExpected_TrimsSuccessfully()
    {
        // Arrange: TPM returns 8 bytes, first 2 are data, rest are zero padding
        var rawOutput = new byte[] { 0xAA, 0xBB, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        SetupSuccessfulDecrypt(rawOutput, actualResult: 8);

        var decryptPolicy = BuildDecryptPolicy();

        // Act
        var result = decryptPolicy.DecryptCore("MyKey", new byte[256], expectedLength: 2);

        // Assert
        Assert.Equal(new byte[] { 0xAA, 0xBB }, result);
    }

    [Fact]
    public void DecryptExact_ShortOutput_Throws()
    {
        // Arrange: TPM returns 3 bytes but expected length is 4
        var rawOutput = new byte[] { 0x01, 0x02, 0x03 };
        SetupSuccessfulDecrypt(rawOutput, actualResult: 3);

        var decryptPolicy = BuildDecryptPolicy();

        // Act & Assert
        Assert.Throws<CryptographicException>(() => decryptPolicy.DecryptCore("MyKey", new byte[256], expectedLength: 4));
    }

    [Fact]
    public void DecryptExact_NonZeroTrailingBytes_Throws()
    {
        // Arrange: TPM returns 4 bytes, expected 2, but bytes 2-3 are non-zero
        var rawOutput = new byte[] { 0x01, 0x02, 0x00, 0xFF };
        SetupSuccessfulDecrypt(rawOutput, actualResult: 4);

        var decryptPolicy = BuildDecryptPolicy();

        // Act & Assert
        Assert.Throws<CryptographicException>(() => decryptPolicy.DecryptCore("MyKey", new byte[256], expectedLength: 2));
    }

    [Fact]
    public void DecryptExact_ZeroExpectedLength_Throws()
    {
        // Arrange: any output, expected length is 0 (invalid)
        var rawOutput = new byte[] { 0x01 };
        SetupSuccessfulDecrypt(rawOutput, actualResult: 1);

        var decryptPolicy = BuildDecryptPolicy();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => decryptPolicy.DecryptCore("MyKey", new byte[256], expectedLength: 0));
    }

    // ── Delegate types for Moq callbacks ─────────────────────────────────────

    private delegate void OpenStorageProviderCallback(out IntPtr phProvider, string pszProviderName, int dwFlags);

    private delegate void OpenKeyCallback(IntPtr hProvider, out IntPtr phKey, string pszKeyName, int dwLegacyKeySpec, int dwFlags);

    private delegate void CreatePersistedKeyCallback(IntPtr hProvider, out IntPtr phKey, string pszAlgId, string? pszKeyName, int dwLegacyKeySpec, int dwFlags);

    private delegate void ExportKeySizeCallback(IntPtr hKey, IntPtr hExportKey, string pszBlobType, IntPtr pParameterList, byte[]? pbOutput, int cbOutput, out int pcbResult, int dwFlags);

    private delegate void ExportKeyDataCallback(IntPtr hKey, IntPtr hExportKey, string pszBlobType, IntPtr pParameterList, byte[]? pbOutput, int cbOutput, out int pcbResult, int dwFlags);

    private delegate void DecryptCallback(IntPtr hKey, byte[] pbInput, int cbInput, ref OaepPaddingInfo pPaddingInfo, byte[]? pbOutput, int cbOutput, out int pcbResult, int dwFlags);
}
