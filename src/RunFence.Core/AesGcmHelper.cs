using System.Security.Cryptography;

namespace RunFence.Core;

/// <summary>
/// Shared static class for raw AES-256-GCM encrypt/decrypt.
/// Format: nonce(12B) + tag(16B) + ciphertext.
/// Key zeroing is caller's responsibility.
/// </summary>
public static class AesGcmHelper
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>
    /// Encrypts plaintext using AES-256-GCM.
    /// Returns nonce(12B) + tag(16B) + ciphertext. Random nonce via RandomNumberGenerator.
    /// </summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> aad = default)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);

        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize + TagSize, ciphertext.Length);
        return result;
    }

    /// <summary>
    /// Decrypts AES-256-GCM encrypted data (nonce(12B) + tag(16B) + ciphertext).
    /// Throws CryptographicException on tampering or wrong key.
    /// </summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> encrypted, ReadOnlySpan<byte> key, ReadOnlySpan<byte> aad = default)
    {
        if (encrypted.Length < NonceSize + TagSize)
            throw new CryptographicException("Encrypted data is too short.");

        var nonce = encrypted[..NonceSize];
        var tag = encrypted.Slice(NonceSize, TagSize);
        var ciphertext = encrypted[(NonceSize + TagSize)..];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
        return plaintext;
    }
}
