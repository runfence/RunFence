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
        _service = new CredentialEncryptionService();
        _pinDerivedKey = new byte[32];
        new Random(99).NextBytes(_pinDerivedKey);
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip()
    {
        var password = CreateProtectedString("TestPassword123!");

        var encrypted = _service.Encrypt(password, _pinDerivedKey);
        Assert.NotEmpty(encrypted);

        var decrypted = _service.Decrypt(encrypted, _pinDerivedKey);
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

        var decrypted1 = ProtectedStringToString(_service.Decrypt(encrypted1, _pinDerivedKey));
        var decrypted2 = ProtectedStringToString(_service.Decrypt(encrypted2, _pinDerivedKey));
        Assert.Equal(decrypted1, decrypted2);
    }

    [Fact]
    public void Decrypt_EmptyPassword_RoundTrips()
    {
        // Arrange: empty ProtectedString (zero-length = 0 chars)
        var empty = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);
        empty.MakeReadOnly();

        var encrypted = _service.Encrypt(empty, _pinDerivedKey);
        var decrypted = _service.Decrypt(encrypted, _pinDerivedKey);

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

        var decrypted = _service.Decrypt(encrypted, _pinDerivedKey);

        Assert.Equal(passwordValue, ProtectedStringToString(decrypted));
    }

    private static ProtectedString CreateProtectedString(string value)
        => new(value.AsSpan(), protect: false);

    private static string ProtectedStringToString(ProtectedString ps)
    {
        var ptr = ps.AllocUnicode();
        try
        {
            return Marshal.PtrToStringUni(ptr)!;
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }
}