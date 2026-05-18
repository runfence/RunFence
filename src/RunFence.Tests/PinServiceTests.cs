using System.Runtime.InteropServices;
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
        _encryptionService = new CredentialEncryptionService(new NativeDpapiProtector());
        // Use minimal Argon2 parameters for fast tests (1 MB, 1 iteration)
        _pinService = new PinService(_encryptionService, argon2MemoryKb: 1024, argon2Iterations: 1, argon2Parallelism: 1);
    }

    private static (CredentialStore store, byte[] key) UnpackReset(PinResetResult result)
    {
        using (result)
        {
            using var key = result.TakePinDerivedKey();
            return (result.Store, key.TransformSnapshot(data => data.ToArray()));
        }
    }

    private byte[] DeriveKeyBytes(ProtectedString pin, byte[] salt)
    {
        using var key = _pinService.DeriveKeySecret(pin, salt);
        return key.TransformSnapshot(data => data.ToArray());
    }

    private bool VerifyPinForBytes(ProtectedString pin, CredentialStore store, out byte[] pinDerivedKey)
    {
        using var verification = _pinService.VerifyPinForSession(pin, store);
        if (!verification.Succeeded)
        {
            pinDerivedKey = [];
            return false;
        }

        using var key = verification.TakePinDerivedKey();
        pinDerivedKey = key.TransformSnapshot(data => data.ToArray());
        return true;
    }

    private (CredentialStore store, byte[] newPinDerivedKey) ChangePinForBytes(
        byte[] oldPinDerivedKey,
        ProtectedString newPin,
        CredentialStore store)
    {
        using var oldKey = TestSecretFactory.FromBytes(oldPinDerivedKey);
        using var result = _pinService.ChangePin(oldKey, newPin, store);
        using var key = result.TakeNewPinDerivedKey();
        return (result.Store, key.TransformSnapshot(data => data.ToArray()));
    }

    [Fact]
    public void DeriveKey_SameInputs_ReturnsSameKey()
    {
        var salt = new byte[32];
        new Random(42).NextBytes(salt);

        using var pin = ProtectedString.FromChars("testpin".AsSpan());
        var key1 = DeriveKeyBytes(pin, salt);
        var key2 = DeriveKeyBytes(pin, salt);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void DeriveKey_DifferentPins_ReturnsDifferentKeys()
    {
        var salt = new byte[32];
        new Random(42).NextBytes(salt);

        using var pin1 = ProtectedString.FromChars("pin1".AsSpan());
        using var pin2 = ProtectedString.FromChars("pin2".AsSpan());
        var key1 = DeriveKeyBytes(pin1, salt);
        var key2 = DeriveKeyBytes(pin2, salt);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void DeriveKey_DifferentSalts_ReturnsDifferentKeys()
    {
        var salt1 = new byte[32];
        var salt2 = new byte[32];
        new Random(42).NextBytes(salt1);
        new Random(99).NextBytes(salt2);

        using var pin = ProtectedString.FromChars("testpin".AsSpan());
        var key1 = DeriveKeyBytes(pin, salt1);
        var key2 = DeriveKeyBytes(pin, salt2);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void DeriveKey_ReturnsCorrectLength()
    {
        var salt = new byte[32];
        new Random(42).NextBytes(salt);

        using var pin = ProtectedString.FromChars("testpin".AsSpan());
        var key = DeriveKeyBytes(pin, salt);
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void VerifyPin_WrongPin_ReturnsFalse()
    {
        using var correctPin = ProtectedString.FromChars("correctpin".AsSpan());
        var (store, _) = UnpackReset(_pinService.ResetPin(correctPin));

        using var wrongPin = ProtectedString.FromChars("wrongpin".AsSpan());
        var verified = VerifyPinForBytes(wrongPin, store, out var key);
        Assert.False(verified);
        Assert.Empty(key);
    }

    [Fact]
    public void ChangePin_WithOldKey_VerifiesCanaryBeforeProceeding()
    {
        using var testPin = ProtectedString.FromChars("testpin".AsSpan());
        var (store, _) = UnpackReset(_pinService.ResetPin(testPin));
        VerifyPinForBytes(testPin, store, out var oldKey);

        // Should succeed with correct old key
        using var newPin = ProtectedString.FromChars("newpin".AsSpan());
        var (newStore, newKey) = ChangePinForBytes(oldKey, newPin, store);
        Assert.NotNull(newStore);
        Assert.NotEmpty(newKey);
    }

    [Fact]
    public void ChangePin_WithOldKey_WrongKey_Throws()
    {
        using var correctPin = ProtectedString.FromChars("correctpin".AsSpan());
        var (store, _) = UnpackReset(_pinService.ResetPin(correctPin));

        var wrongKey = new byte[32];
        new Random(77).NextBytes(wrongKey);

        using var newPin = ProtectedString.FromChars("newpin".AsSpan());
        using var wrongKeySecret = TestSecretFactory.FromBytes(wrongKey);
        Assert.Throws<CryptographicException>(() =>
            _pinService.ChangePin(wrongKeySecret, newPin, store));
    }

    [Fact]
    public void ChangePin_ReencryptsCredentials()
    {
        using var oldPin = ProtectedString.FromChars("oldpin".AsSpan());
        var (store, _) = UnpackReset(_pinService.ResetPin(oldPin));

        // Add a credential with encrypted password
        VerifyPinForBytes(oldPin, store, out var oldKey);
        using var password = new ProtectedString("TestPassword".AsSpan(), protect: false);
        var encrypted = _encryptionService.Encrypt(password, oldKey);
        store.Credentials.Add(new CredentialEntry
        {
            Id = Guid.NewGuid(),
            Sid = "S-1-5-21-0-0-0-1001",
            EncryptedPassword = encrypted
        });

        using var newPin = ProtectedString.FromChars("newpin".AsSpan());
        var (newStore, newKey) = ChangePinForBytes(oldKey, newPin, store);

        Assert.NotNull(newStore);
        Assert.NotEqual(store.ArgonSalt, newStore.ArgonSalt);
        Assert.Single(newStore.Credentials);
        Assert.NotEqual(encrypted, newStore.Credentials[0].EncryptedPassword);
        Assert.Equal(32, newKey.Length);

        // Verify the re-encrypted password can be decrypted with the new key
        using var decrypted = _encryptionService.Decrypt(newStore.Credentials[0].EncryptedPassword, newKey);
        var result = decrypted.UseUnicodeSnapshot(snapshot =>
            Marshal.PtrToStringUni(snapshot.DangerousGetIntPtr(), snapshot.CharCount));
        Assert.Equal("TestPassword", result);
    }

    [Fact]
    public void ChangePin_PreservesCurrentAccountCredentials()
    {
        using var oldPin = ProtectedString.FromChars("oldpin".AsSpan());
        var (store, _) = UnpackReset(_pinService.ResetPin(oldPin));
        store.Credentials.Add(new CredentialEntry
        {
            Id = Guid.NewGuid(),
            Sid = SidResolutionHelper.GetCurrentUserSid(),
            EncryptedPassword = Array.Empty<byte>()
        });

        VerifyPinForBytes(oldPin, store, out var oldKey);
        using var newPin = ProtectedString.FromChars("newpin".AsSpan());
        var (newStore, _) = ChangePinForBytes(oldKey, newPin, store);

        Assert.Single(newStore.Credentials);
        Assert.True(newStore.Credentials[0].IsCurrentAccount);
        Assert.Empty(newStore.Credentials[0].EncryptedPassword);
    }

    [Fact]
    public void ChangePin_ReturnsUsableKey()
    {
        using var oldPin = ProtectedString.FromChars("oldpin".AsSpan());
        var (store, _) = UnpackReset(_pinService.ResetPin(oldPin));
        VerifyPinForBytes(oldPin, store, out var oldKey);
        using var newPin = ProtectedString.FromChars("newpin".AsSpan());
        var (newStore, newKey) = ChangePinForBytes(oldKey, newPin, store);

        // The returned key should verify against the new store
        var verified = VerifyPinForBytes(newPin, newStore, out var verifyKey);
        Assert.True(verified);
        Assert.Equal(newKey, verifyKey);
    }

    [Fact]
    public void ResetPin_CreatesNewStore()
    {
        using var pin = ProtectedString.FromChars("newpin".AsSpan());
        var (store, pinDerivedKey) = UnpackReset(_pinService.ResetPin(pin));

        Assert.NotNull(store);
        Assert.Equal(32, store.ArgonSalt.Length);
        Assert.NotEmpty(store.EncryptedCanary);
        Assert.Empty(store.Credentials);
        Assert.NotNull(pinDerivedKey);

        var verified = VerifyPinForBytes(pin, store, out var key);
        Assert.True(verified);
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void VerifyDerivedKey_CorrectKey_ReturnsTrue()
    {
        using var pin = ProtectedString.FromChars("testpin".AsSpan());
        var (store, pinDerivedKey) = UnpackReset(_pinService.ResetPin(pin));

        var result = _pinService.VerifyDerivedKey(pinDerivedKey, store);

        Assert.True(result);
    }

    [Fact]
    public void VerifyDerivedKey_WrongKey_ReturnsFalse()
    {
        using var pin = ProtectedString.FromChars("testpin".AsSpan());
        var (store, _) = UnpackReset(_pinService.ResetPin(pin));
        var wrongKey = new byte[32];
        new Random(77).NextBytes(wrongKey);

        var result = _pinService.VerifyDerivedKey(wrongKey, store);

        Assert.False(result);
    }

    [Fact]
    public void VerifyDerivedKey_CorruptedCanary_ReturnsFalse()
    {
        using var pin = ProtectedString.FromChars("testpin".AsSpan());
        var (store, pinDerivedKey) = UnpackReset(_pinService.ResetPin(pin));
        store.EncryptedCanary[0] ^= 0xFF;

        var result = _pinService.VerifyDerivedKey(pinDerivedKey, store);

        Assert.False(result);
    }
}
