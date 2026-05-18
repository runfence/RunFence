using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RunFence.Core;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class CredentialEncryptionServiceTests
{
    private readonly CredentialEncryptionService _service;
    private readonly byte[] _pinDerivedKey;

    public CredentialEncryptionServiceTests()
    {
        _service = new CredentialEncryptionService(new NativeDpapiProtector());
        _pinDerivedKey = new byte[32];
        new Random(99).NextBytes(_pinDerivedKey);
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip()
    {
        var password = CreateProtectedString("TestPassword123!");

        var encrypted = _service.Encrypt(password, _pinDerivedKey);
        Assert.NotEmpty(encrypted);

        using var decrypted = _service.Decrypt(encrypted, _pinDerivedKey);
        Assert.NotNull(decrypted);

        Assert.Equal("TestPassword123!", ProtectedStringToString(decrypted));
    }

    [Fact]
    public void Decrypt_WrongPinKey_Throws()
    {
        var password = CreateProtectedString("TestPassword");
        var encrypted = _service.Encrypt(password, _pinDerivedKey);

        var wrongPinKey = new byte[32];
        new Random(777).NextBytes(wrongPinKey);

        Assert.Throws<CryptographicException>(() =>
            _service.Decrypt(encrypted, wrongPinKey));
    }

    [Fact]
    public void Encrypt_ProducesDifferentOutputForSameInput()
    {
        var password1 = CreateProtectedString("SamePassword");
        var password2 = CreateProtectedString("SamePassword");

        var encrypted1 = _service.Encrypt(password1, _pinDerivedKey);
        var encrypted2 = _service.Encrypt(password2, _pinDerivedKey);

        Assert.NotEqual(encrypted1, encrypted2);

        using var decrypted1 = _service.Decrypt(encrypted1, _pinDerivedKey);
        using var decrypted2 = _service.Decrypt(encrypted2, _pinDerivedKey);
        var decryptedValue1 = ProtectedStringToString(decrypted1);
        var decryptedValue2 = ProtectedStringToString(decrypted2);
        Assert.Equal(decryptedValue1, decryptedValue2);
    }

    [Fact]
    public void Decrypt_EmptyPassword_RoundTrips()
    {
        // Arrange: empty ProtectedString (zero-length = 0 chars)
        var empty = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);
        empty.MakeReadOnly();

        var encrypted = _service.Encrypt(empty, _pinDerivedKey);
        using var decrypted = _service.Decrypt(encrypted, _pinDerivedKey);

        Assert.NotNull(decrypted);
        Assert.Equal(0, decrypted.Length);
    }

    [Theory]
    [InlineData("P@ssw0rd!")]
    [InlineData("a")]
    [InlineData("Unicode: \u00e9\u00e0\u00fc")]
    [InlineData("Symbols: ~`!@#$%^&*()-_=+[]{}|;':\",./<>?")]
    public void Decrypt_VariousPasswords_RoundTripPreservesContent(string passwordValue)
    {
        // Decrypt is tested directly: result of Decrypt(Encrypt(x)) must equal x
        var password = CreateProtectedString(passwordValue);
        var encrypted = _service.Encrypt(password, _pinDerivedKey);

        using var decrypted = _service.Decrypt(encrypted, _pinDerivedKey);

        Assert.Equal(passwordValue, ProtectedStringToString(decrypted));
    }

    [Fact]
    public void EncryptDecrypt_SpanInterface_RoundTrip()
    {
        var spanService = (ICredentialEncryptionSpanService)_service;
        var password = CreateProtectedString("SpanPassword123!");

        var encrypted = spanService.Encrypt(password, _pinDerivedKey.AsSpan());
        Assert.NotEmpty(encrypted);

        using var decrypted = spanService.Decrypt(encrypted, _pinDerivedKey.AsSpan());

        Assert.Equal("SpanPassword123!", ProtectedStringToString(decrypted));
    }

    [Fact]
    public void Encrypt_WhenDpapiProtectFails_PropagatesException()
    {
        var expected = new CryptographicException("protect failed");
        var service = new CredentialEncryptionService(new FakeDpapiProtector
        {
            ProtectFunc = (_, _) => throw expected
        });
        var password = CreateProtectedString("TestPassword123!");

        var actual = Assert.Throws<CryptographicException>(() => service.Encrypt(password, _pinDerivedKey));

        Assert.Same(expected, actual);
    }

    [Fact]
    public void Decrypt_WhenDpapiUnprotectFails_PropagatesException()
    {
        var expected = new CryptographicException("unprotect failed");
        var service = new CredentialEncryptionService(new FakeDpapiProtector
        {
            UnprotectToProtectedStringFunc = (_, _) => throw expected
        });

        var actual = Assert.Throws<CryptographicException>(() => service.Decrypt([0x01, 0x02, 0x03], _pinDerivedKey));

        Assert.Same(expected, actual);
    }

    private static ProtectedString CreateProtectedString(string value)
        => new(value.AsSpan(), protect: false);

    private static string ProtectedStringToString(ProtectedString ps)
        => ps.UseUnicodeSnapshot(snapshot =>
            Marshal.PtrToStringUni(snapshot.DangerousGetIntPtr(), snapshot.CharCount) ?? string.Empty);

    private sealed class FakeDpapiProtector : IDpapiProtector
    {
        public Func<byte[], byte[], byte[]>? ProtectFunc { get; init; }
        public Func<byte[], byte[], SecureSecret>? UnprotectToSecretFunc { get; init; }
        public Func<byte[], byte[], ProtectedString>? UnprotectToProtectedStringFunc { get; init; }

        public byte[] Protect(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> entropy)
            => ProtectFunc?.Invoke(plaintext.ToArray(), entropy.ToArray()) ?? plaintext.ToArray();

        public SecureSecret UnprotectToSecret(byte[] protectedData, ReadOnlySpan<byte> entropy)
            => UnprotectToSecretFunc?.Invoke(protectedData, entropy.ToArray())
                ?? throw new NotSupportedException();

        public ProtectedString UnprotectToProtectedString(byte[] protectedData, ReadOnlySpan<byte> entropy)
            => UnprotectToProtectedStringFunc?.Invoke(protectedData, entropy.ToArray())
                ?? throw new NotSupportedException();
    }
}
