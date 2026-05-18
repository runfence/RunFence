using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Security;

public class PinService(
    ICredentialEncryptionSpanService encryptionService,
    int? argon2MemoryKb = null,
    int? argon2Iterations = null,
    int? argon2Parallelism = null)
    : IPinService
{
    private byte[] DeriveKey(ProtectedString pin, byte[] salt)
    {
        byte[]? pinBytes = null;
        var pinnedPinBytes = default(GCHandle);
        pin.UseUtf16BytesSnapshot(
            utf16Bytes =>
            {
                var chars = MemoryMarshal.Cast<byte, char>(utf16Bytes);
                pinBytes = new byte[Encoding.UTF8.GetByteCount(chars)];
                Encoding.UTF8.GetBytes(chars, pinBytes);
                pinnedPinBytes = GCHandle.Alloc(pinBytes, GCHandleType.Pinned);
            });

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
            {
                try
                {
                    CryptographicOperations.ZeroMemory(pinBytes);
                }
                finally
                {
                    if (pinnedPinBytes.IsAllocated)
                        pinnedPinBytes.Free();
                }
            }
        }
    }

    public SecureSecret DeriveKeySecret(ProtectedString pin, byte[] salt)
    {
        var derivedKey = DeriveKey(pin, salt);
        try
        {
            if (derivedKey.Length != Constants.Argon2OutputBytes)
                throw new CryptographicException("Derived PIN key has an invalid length.");

            return new SecureSecret(
                derivedKey.Length,
                destination => derivedKey.CopyTo(destination));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derivedKey);
        }
    }

    private PinResetResult CreateNewStoreWithKey(ProtectedString pin)
    {
        var salt = RandomNumberGenerator.GetBytes(Constants.Argon2SaltSize);
        var pinDerivedKey = DeriveKeySecret(pin, salt);

        try
        {
            var encryptedCanary = pinDerivedKey.TransformSnapshot(EncryptCanary);
            var store = new CredentialStore
            {
                ArgonSalt = salt,
                EncryptedCanary = encryptedCanary,
                Credentials = new List<CredentialEntry>()
            };

            return new PinResetResult(store, pinDerivedKey);
        }
        catch
        {
            pinDerivedKey.Dispose();
            throw;
        }
    }

    public bool VerifyDerivedKey(ReadOnlySpan<byte> pinDerivedKey, CredentialStore store)
    {
        try
        {
            var decrypted = DecryptCanary(store.EncryptedCanary, pinDerivedKey);
            return CryptographicOperations.FixedTimeEquals(decrypted, Constants.PinCanaryPlaintext);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public bool VerifyPin(ProtectedString pin, CredentialStore store)
    {
        using var result = VerifyPinForSession(pin, store);
        return result.Succeeded;
    }

    public PinVerificationResult VerifyPinForSession(ProtectedString pin, CredentialStore store)
    {
        SecureSecret? pinDerivedKey = null;
        try
        {
            pinDerivedKey = DeriveKeySecret(pin, store.ArgonSalt);
            bool verified = pinDerivedKey.TransformSnapshot(key => VerifyDerivedKey(key, store));
            if (verified)
                return PinVerificationResult.Success(pinDerivedKey);

            pinDerivedKey.Dispose();
            pinDerivedKey = null;
            return PinVerificationResult.Failed();
        }
        catch
        {
            pinDerivedKey?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Changes the PIN. Verifies the old key by decrypting the canary (defense-in-depth),
    /// then re-encrypts all credentials with the new key.
    /// Zeros newPinDerivedKey on all error paths.
    /// </summary>
    public PinKeyRotationResult ChangePin(ISecureSecretSnapshotSource oldPinDerivedKey, ProtectedString newPin, CredentialStore store)
    {
        ArgumentNullException.ThrowIfNull(oldPinDerivedKey);

        // Defense-in-depth: verify old key before proceeding
        bool verified = oldPinDerivedKey.TransformSnapshot(key => VerifyDerivedKey(key, store));
        if (!verified)
            throw new CryptographicException("Old PIN key is incorrect.");

        var newSalt = RandomNumberGenerator.GetBytes(Constants.Argon2SaltSize);
        var newPinDerivedKey = DeriveKeySecret(newPin, newSalt);

        try
        {
            var encryptedCanary = newPinDerivedKey.TransformSnapshot(EncryptCanary);
            var newStore = new CredentialStore
            {
                ArgonSalt = newSalt,
                EncryptedCanary = encryptedCanary,
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
                    password = oldPinDerivedKey.TransformSnapshot(key =>
                        encryptionService.Decrypt(cred.EncryptedPassword, key));
                    var newEncrypted = newPinDerivedKey.TransformSnapshot(key => encryptionService.Encrypt(password, key));
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

            return new PinKeyRotationResult(newStore, newPinDerivedKey);
        }
        catch
        {
            newPinDerivedKey.Dispose();
            throw;
        }
    }

    public PinResetResult ResetPin(ProtectedString newPin) => CreateNewStoreWithKey(newPin);

    private static byte[] EncryptCanary(ReadOnlySpan<byte> pinDerivedKey)
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

    private static byte[] DecryptCanary(byte[] encryptedCanary, ReadOnlySpan<byte> pinDerivedKey)
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
