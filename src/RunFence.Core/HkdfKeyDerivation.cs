using System.Security.Cryptography;

namespace RunFence.Core;

/// <summary>
/// Derives domain-separated sub-keys from pinDerivedKey using HKDF-Expand.
/// Uses Expand (not DeriveKey) because pinDerivedKey is already a PRK — Argon2id output
/// is uniform random, so the HKDF-Extract step would be redundant.
/// </summary>
public static class HkdfKeyDerivation
{
    private static readonly byte[] DpapiEntropyInfo = "dpapi-entropy"u8.ToArray();
    private static readonly byte[] ConfigEncryptionInfo = "config-encryption"u8.ToArray();
    private static readonly byte[] CanaryEncryptionInfo = "canary-encryption"u8.ToArray();

    /// <summary>
    /// Derives a 32-byte key for use as DPAPI additional entropy.
    /// </summary>
    public static byte[] DeriveDpapiEntropy(ReadOnlySpan<byte> pinDerivedKey)
    {
        byte[] derived = new byte[32];
        HKDF.Expand(HashAlgorithmName.SHA256, pinDerivedKey, derived, DpapiEntropyInfo);
        return derived;
    }

    /// <summary>
    /// Derives a 32-byte AES-256 key for encrypting config files.
    /// </summary>
    public static byte[] DeriveConfigEncryptionKey(ReadOnlySpan<byte> pinDerivedKey)
    {
        byte[] derived = new byte[32];
        HKDF.Expand(HashAlgorithmName.SHA256, pinDerivedKey, derived, ConfigEncryptionInfo);
        return derived;
    }

    /// <summary>
    /// Derives a 32-byte AES-256 key for encrypting the PIN canary.
    /// </summary>
    public static byte[] DeriveCanaryEncryptionKey(ReadOnlySpan<byte> pinDerivedKey)
    {
        byte[] derived = new byte[32];
        HKDF.Expand(HashAlgorithmName.SHA256, pinDerivedKey, derived, CanaryEncryptionInfo);
        return derived;
    }
}
