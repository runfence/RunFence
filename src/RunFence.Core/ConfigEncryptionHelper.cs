using System.Security.Cryptography;

namespace RunFence.Core;

/// <summary>
/// RAME v2 encrypted config file format:
///   RAME(4B) + version(1B) + fileType(1B) + argonSalt(32B) + nonce(12B) + tag(16B) + ciphertext
///   Total prefix: 38B static header (used as AAD). Minimum file size: 66B.
/// AAD: magic(4) + version(1) + fileType(1) + salt(32) = 38 bytes — prevents cross-file substitution
/// and ensures any salt byte change invalidates the GCM tag.
/// v1 files are rejected at decryption time (CryptographicException).
/// </summary>
public static class ConfigEncryptionHelper
{
    private static readonly byte[] Magic = "RAME"u8.ToArray();
    private const byte FormatVersion = 0x02;
    private const int HeaderSize = 6; // magic(4) + version(1) + fileType(1) — static prefix
    private const int SaltSize = 32;
    private const int MinEncryptedSize = 66; // HeaderSize(6) + SaltSize(32) + nonce(12B) + tag(16B)

    public static byte[] EncryptConfig(byte[] plaintext, byte[] masterKey, ConfigFileType fileType, byte[] argonSalt)
    {
        // Build static prefix + salt (38 bytes) — this array is also the AAD
        var aad = new byte[HeaderSize + SaltSize];
        Buffer.BlockCopy(Magic, 0, aad, 0, Magic.Length);
        aad[4] = FormatVersion;
        aad[5] = (byte)fileType;
        Buffer.BlockCopy(argonSalt, 0, aad, HeaderSize, SaltSize);

        byte[]? derivedKey = null;
        try
        {
            derivedKey = HkdfKeyDerivation.DeriveConfigEncryptionKey(masterKey);
            var encrypted = AesGcmHelper.Encrypt(plaintext, derivedKey, aad);

            var result = new byte[aad.Length + encrypted.Length];
            Buffer.BlockCopy(aad, 0, result, 0, aad.Length);
            Buffer.BlockCopy(encrypted, 0, result, aad.Length, encrypted.Length);
            return result;
        }
        finally
        {
            if (derivedKey != null)
                CryptographicOperations.ZeroMemory(derivedKey);
        }
    }

    public static byte[] DecryptConfig(byte[] encrypted, byte[] masterKey, ConfigFileType fileType)
    {
        if (encrypted.Length < MinEncryptedSize)
            throw new CryptographicException("Config file is too short.");

        if (encrypted[0] != Magic[0] || encrypted[1] != Magic[1] ||
            encrypted[2] != Magic[2] || encrypted[3] != Magic[3])
            throw new CryptographicException("Config file is not encrypted.");

        var version = encrypted[4];
        if (version != FormatVersion)
            throw new CryptographicException($"Unsupported config version: {version}");

        if (encrypted[5] != (byte)fileType)
            throw new CryptographicException("Config file type mismatch.");

        // AAD = entire static prefix + salt (first 38 bytes)
        var aad = encrypted[..(HeaderSize + SaltSize)];
        var cipherBlob = encrypted[(HeaderSize + SaltSize)..];

        byte[]? derivedKey = null;
        try
        {
            derivedKey = HkdfKeyDerivation.DeriveConfigEncryptionKey(masterKey);
            return AesGcmHelper.Decrypt(cipherBlob, derivedKey, aad);
        }
        finally
        {
            if (derivedKey != null)
                CryptographicOperations.ZeroMemory(derivedKey);
        }
    }

    /// <summary>
    /// Extracts the embedded Argon2 salt from a v2 RAME file header.
    /// Returns null if the data is too short, has wrong magic, or is not v2.
    /// Never throws.
    /// </summary>
    public static byte[]? TryExtractArgonSalt(byte[] data)
    {
        if (data.Length < 4)
            return null;
        if (data[0] != Magic[0] || data[1] != Magic[1] ||
            data[2] != Magic[2] || data[3] != Magic[3])
            return null;
        if (data.Length < HeaderSize + SaltSize)
            return null; // < 38, also guards data[4] access
        if (data[4] != FormatVersion)
            return null;

        var salt = new byte[SaltSize];
        Buffer.BlockCopy(data, HeaderSize, salt, 0, SaltSize);
        return salt;
    }

    public static bool HasEncryptionHeader(byte[] data)
    {
        if (data.Length < 4)
            return false;
        return data[0] == Magic[0] && data[1] == Magic[1] &&
               data[2] == Magic[2] && data[3] == Magic[3];
    }
}