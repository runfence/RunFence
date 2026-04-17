using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class PinServiceTests
{
    private readonly PinService _pinService;
    private readonly CredentialEncryptionService _encryptionService;

    public PinServiceTests()
    {
        _encryptionService = new CredentialEncryptionService();
        // Use minimal Argon2 parameters for fast tests (1 MB, 1 iteration)
        _pinService = new PinService(_encryptionService, argon2MemoryKb: 1024, argon2Iterations: 1, argon2Parallelism: 1);
    }

    [Fact]
    public void DeriveKey_SameInputs_ReturnsSameKey()
    {
        var salt = new byte[32];
        new Random(42).NextBytes(salt);

        var key1 = _pinService.DeriveKey("testpin", salt);
        var key2 = _pinService.DeriveKey("testpin", salt);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void DeriveKey_DifferentPins_ReturnsDifferentKeys()
    {
        var salt = new byte[32];
        new Random(42).NextBytes(salt);

        var key1 = _pinService.DeriveKey("pin1", salt);
        var key2 = _pinService.DeriveKey("pin2", salt);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void DeriveKey_DifferentSalts_ReturnsDifferentKeys()
    {
        var salt1 = new byte[32];
        var salt2 = new byte[32];
        new Random(42).NextBytes(salt1);
        new Random(99).NextBytes(salt2);

        var key1 = _pinService.DeriveKey("testpin", salt1);
        var key2 = _pinService.DeriveKey("testpin", salt2);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void DeriveKey_ReturnsCorrectLength()
    {
        var salt = new byte[32];
        new Random(42).NextBytes(salt);

        var key = _pinService.DeriveKey("testpin", salt);
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void VerifyPin_WrongPin_ReturnsFalse()
    {
        var (store, _) = _pinService.ResetPin("correctpin");

        var verified = _pinService.VerifyPin("wrongpin", store, out var key);
        Assert.False(verified);
        Assert.Empty(key);
    }

    [Fact]
    public void ChangePin_WithOldKey_VerifiesCanaryBeforeProceeding()
    {
        var (store, _) = _pinService.ResetPin("testpin");
        _pinService.VerifyPin("testpin", store, out var oldKey);

        // Should succeed with correct old key
        var (newStore, newKey) = _pinService.ChangePin(oldKey, "newpin", store);
        Assert.NotNull(newStore);
        Assert.NotEmpty(newKey);
    }

    [Fact]
    public void ChangePin_WithOldKey_WrongKey_Throws()
    {
        var (store, _) = _pinService.ResetPin("correctpin");

        var wrongKey = new byte[32];
        new Random(77).NextBytes(wrongKey);

        Assert.Throws<CryptographicException>(() =>
            _pinService.ChangePin(wrongKey, "newpin", store));
    }

    [Fact]
    public void ChangePin_ReencryptsCredentials()
    {
        var (store, _) = _pinService.ResetPin("oldpin");

        // Add a credential with encrypted password
        _pinService.VerifyPin("oldpin", store, out var oldKey);
        var password = new SecureString();
        foreach (var c in "TestPassword")
            password.AppendChar(c);
        password.MakeReadOnly();

        var encrypted = _encryptionService.Encrypt(password, oldKey);
        store.Credentials.Add(new CredentialEntry
        {
            Id = Guid.NewGuid(),
            Sid = "S-1-5-21-0-0-0-1001",
            EncryptedPassword = encrypted
        });

        var (newStore, newKey) = _pinService.ChangePin(oldKey, "newpin", store);

        Assert.NotNull(newStore);
        Assert.NotEqual(store.ArgonSalt, newStore.ArgonSalt);
        Assert.Single(newStore.Credentials);
        Assert.NotEqual(encrypted, newStore.Credentials[0].EncryptedPassword);
        Assert.Equal(32, newKey.Length);

        // Verify the re-encrypted password can be decrypted with the new key
        var decrypted = _encryptionService.Decrypt(newStore.Credentials[0].EncryptedPassword, newKey);
        var bstr = Marshal.SecureStringToBSTR(decrypted);
        try
        {
            var result = Marshal.PtrToStringBSTR(bstr);
            Assert.Equal("TestPassword", result);
        }
        finally
        {
            Marshal.ZeroFreeBSTR(bstr);
        }
    }

    [Fact]
    public void ChangePin_PreservesCurrentAccountCredentials()
    {
        var (store, _) = _pinService.ResetPin("oldpin");
        store.Credentials.Add(new CredentialEntry
        {
            Id = Guid.NewGuid(),
            Sid = SidResolutionHelper.GetCurrentUserSid(),
            EncryptedPassword = Array.Empty<byte>()
        });

        _pinService.VerifyPin("oldpin", store, out var oldKey);
        var (newStore, _) = _pinService.ChangePin(oldKey, "newpin", store);

        Assert.Single(newStore.Credentials);
        Assert.True(newStore.Credentials[0].IsCurrentAccount);
        Assert.Empty(newStore.Credentials[0].EncryptedPassword);
    }

    [Fact]
    public void ChangePin_ReturnsUsableKey()
    {
        var (store, _) = _pinService.ResetPin("oldpin");
        _pinService.VerifyPin("oldpin", store, out var oldKey);
        var (newStore, newKey) = _pinService.ChangePin(oldKey, "newpin", store);

        // The returned key should verify against the new store
        var verified = _pinService.VerifyPin("newpin", newStore, out var verifyKey);
        Assert.True(verified);
        Assert.Equal(newKey, verifyKey);
    }

    [Fact]
    public void ResetPin_CreatesNewStore()
    {
        var (store, pinDerivedKey) = _pinService.ResetPin("newpin");

        Assert.NotNull(store);
        Assert.Equal(32, store.ArgonSalt.Length);
        Assert.NotEmpty(store.EncryptedCanary);
        Assert.Empty(store.Credentials);
        Assert.NotNull(pinDerivedKey);

        var verified = _pinService.VerifyPin("newpin", store, out var key);
        Assert.True(verified);
        Assert.Equal(32, key.Length);
    }
}