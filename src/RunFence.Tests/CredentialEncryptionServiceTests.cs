using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
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
        var password = CreateSecureString("TestPassword123!");

        var encrypted = _service.Encrypt(password, _pinDerivedKey);
        Assert.NotEmpty(encrypted);

        var decrypted = _service.Decrypt(encrypted, _pinDerivedKey);
        Assert.NotNull(decrypted);

        Assert.Equal("TestPassword123!", SecureStringToString(decrypted));
    }

    [Fact]
    public void Decrypt_WrongPinKey_Throws()
    {
        var password = CreateSecureString("TestPassword");
        var encrypted = _service.Encrypt(password, _pinDerivedKey);

        var wrongPinKey = new byte[32];
        new Random(777).NextBytes(wrongPinKey);

        Assert.Throws<CryptographicException>(() =>
            _service.Decrypt(encrypted, wrongPinKey));
    }

    [Fact]
    public void Encrypt_ProducesDifferentOutputForSameInput()
    {
        var password1 = CreateSecureString("SamePassword");
        var password2 = CreateSecureString("SamePassword");

        var encrypted1 = _service.Encrypt(password1, _pinDerivedKey);
        var encrypted2 = _service.Encrypt(password2, _pinDerivedKey);

        Assert.NotEqual(encrypted1, encrypted2);

        var decrypted1 = SecureStringToString(_service.Decrypt(encrypted1, _pinDerivedKey));
        var decrypted2 = SecureStringToString(_service.Decrypt(encrypted2, _pinDerivedKey));
        Assert.Equal(decrypted1, decrypted2);
    }

    private static SecureString CreateSecureString(string value)
    {
        var ss = new SecureString();
        foreach (var c in value)
            ss.AppendChar(c);
        ss.MakeReadOnly();
        return ss;
    }

    private static string SecureStringToString(SecureString ss)
    {
        var bstr = Marshal.SecureStringToBSTR(ss);
        try
        {
            return Marshal.PtrToStringBSTR(bstr);
        }
        finally
        {
            Marshal.ZeroFreeBSTR(bstr);
        }
    }
}