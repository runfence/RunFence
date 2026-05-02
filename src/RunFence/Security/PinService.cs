using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Security;

public class PinService(
    ICredentialEncryptionService encryptionService,
    int? argon2MemoryKb = null,
    int? argon2Iterations = null,
    int? argon2Parallelism = null)
    : IPinService
{
    public unsafe byte[] DeriveKey(ProtectedString pin, byte[] salt)
    {
        byte[]? pinBytes = null;
        GCHandle pinHandle = default;
        var utf16Ptr = pin.AllocUnicode();
        try
        {
            var chars = (char*)utf16Ptr;
            var byteCount = Encoding.UTF8.GetByteCount(chars, pin.Length);
            pinBytes = new byte[byteCount];
            pinHandle = GCHandle.Alloc(pinBytes, GCHandleType.Pinned);
            fixed (byte* bytes = pinBytes)
                Encoding.UTF8.GetBytes(chars, pin.Length, bytes, byteCount);
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(utf16Ptr);
        }

        try
        {
            var parameters = new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
                .WithVersion(Argon2Parameters.Version13)
                .WithSalt(salt)
                .WithMemoryAsKB(argon2MemoryKb ?? (Constants.Argon2MemoryMb * 1024))
                .WithIterations(argon2Iterations ?? Constants.Argon2Iterations)
                .WithParallelism(argon2Parallelism ?? Constants.Argon2Parallelism)
                .Build();
            var generator = new Argon2BytesGenerator();
            generator.Init(parameters);
            var output = new byte[Constants.Argon2OutputBytes];
            generator.GenerateBytes(pinBytes!, output);
            // Argon2 allocates ~1.1GB of managed Block objects that get promoted to Gen2
            // during the lengthy computation. In a desktop app, Gen2 rarely triggers naturally,
            // so this dead memory would persist for hours. Force collection immediately.
            // ReSharper disable once RedundantAssignment
            generator = null!;
            // ReSharper disable once RedundantAssignment
            parameters = null!;
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            return output;
        }
        finally
        {
            if (pinBytes != null)
                Array.Clear(pinBytes, 0, pinBytes.Length);
            if (pinHandle.IsAllocated)
                pinHandle.Free();
        }
    }

    private (CredentialStore store, byte[] pinDerivedKey) CreateNewStoreWithKey(ProtectedString pin)
    {
        var salt = RandomNumberGenerator.GetBytes(Constants.Argon2SaltSize);
        var pinDerivedKey = DeriveKey(pin, salt);

        var store = new CredentialStore
        {
            ArgonSalt = salt,
            EncryptedCanary = EncryptCanary(pinDerivedKey),
            Credentials = new List<CredentialEntry>()
        };

        return (store, pinDerivedKey);
    }

    public bool VerifyDerivedKey(byte[] pinDerivedKey, CredentialStore store)
    {
        try
        {
            var decrypted = DecryptCanary(store.EncryptedCanary, pinDerivedKey);
            return CryptographicOperations.FixedTimeEquals(decrypted, Constants.PinCanaryPlaintext);
        }
        catch (CryptographicException) { return false; }
    }

    public bool VerifyPin(ProtectedString pin, CredentialStore store, out byte[] pinDerivedKey)
    {
        pinDerivedKey = DeriveKey(pin, store.ArgonSalt);

        try
        {
            var decrypted = DecryptCanary(store.EncryptedCanary, pinDerivedKey);
            if (CryptographicOperations.FixedTimeEquals(decrypted, Constants.PinCanaryPlaintext))
                return true;

            Array.Clear(pinDerivedKey, 0, pinDerivedKey.Length);
            pinDerivedKey = Array.Empty<byte>();
            return false;
        }
        catch (CryptographicException)
        {
            Array.Clear(pinDerivedKey, 0, pinDerivedKey.Length);
            pinDerivedKey = Array.Empty<byte>();
            return false;
        }
    }

    /// <summary>
    /// Changes the PIN. Verifies the old key by decrypting the canary (defense-in-depth),
    /// then re-encrypts all credentials with the new key.
    /// Zeros newPinDerivedKey on all error paths.
    /// </summary>
    public (CredentialStore store, byte[] newPinDerivedKey) ChangePin(byte[] oldPinDerivedKey, ProtectedString newPin, CredentialStore store)
    {
        // Defense-in-depth: verify old key before proceeding
        try
        {
            var decrypted = DecryptCanary(store.EncryptedCanary, oldPinDerivedKey);
            if (!CryptographicOperations.FixedTimeEquals(decrypted, Constants.PinCanaryPlaintext))
                throw new CryptographicException("Old PIN key is incorrect.");
        }
        catch (CryptographicException)
        {
            throw new CryptographicException("Old PIN key is incorrect.");
        }

        var newSalt = RandomNumberGenerator.GetBytes(Constants.Argon2SaltSize);
        var newPinDerivedKey = DeriveKey(newPin, newSalt);

        try
        {
            var newStore = new CredentialStore
            {
                ArgonSalt = newSalt,
                EncryptedCanary = EncryptCanary(newPinDerivedKey),
                Credentials = new List<CredentialEntry>()
            };

            foreach (var cred in store.Credentials)
            {
                if (cred.IsCurrentAccount || cred.EncryptedPassword.Length == 0)
                {
                    newStore.Credentials.Add(cred);
                    continue;
                }

                ProtectedString? password = null;
                try
                {
                    password = encryptionService.Decrypt(cred.EncryptedPassword, oldPinDerivedKey);
                    var newEncrypted = encryptionService.Encrypt(password, newPinDerivedKey);
                    newStore.Credentials.Add(new CredentialEntry
                    {
                        Id = cred.Id,
                        Sid = cred.Sid,
                        EncryptedPassword = newEncrypted
                    });
                }
                finally
                {
                    password?.Dispose();
                }
            }

            return (newStore, newPinDerivedKey);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(newPinDerivedKey);
            throw;
        }
    }

    public (CredentialStore store, byte[] pinDerivedKey) ResetPin(ProtectedString newPin)
    {
        return CreateNewStoreWithKey(newPin);
    }

    private static byte[] EncryptCanary(byte[] pinDerivedKey)
    {
        byte[]? derivedKey = null;
        try
        {
            derivedKey = HkdfKeyDerivation.DeriveCanaryEncryptionKey(pinDerivedKey);
            return AesGcmHelper.Encrypt(Constants.PinCanaryPlaintext, derivedKey);
        }
        finally
        {
            if (derivedKey != null)
                CryptographicOperations.ZeroMemory(derivedKey);
        }
    }

    private static byte[] DecryptCanary(byte[] encryptedCanary, byte[] pinDerivedKey)
    {
        byte[]? derivedKey = null;
        try
        {
            derivedKey = HkdfKeyDerivation.DeriveCanaryEncryptionKey(pinDerivedKey);
            return AesGcmHelper.Decrypt(encryptedCanary, derivedKey);
        }
        finally
        {
            if (derivedKey != null)
                CryptographicOperations.ZeroMemory(derivedKey);
        }
    }
}
