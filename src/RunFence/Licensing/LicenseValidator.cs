using System.Security.Cryptography;
using System.Text;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Licensing;

/// <summary>
/// Pure ECDsa P-256 license key validator. No dependencies — fully unit-testable.
/// The production public key is embedded here. Tests use a separate key pair.
/// </summary>
internal class LicenseValidator
{
    // Production ECDsa P-256 public key in SubjectPublicKeyInfo (DER) format, base64-encoded.
    // The corresponding private key is stored in the RunFence.Private repository.
    // IMPORTANT: If you lose the private key, no new license keys can be generated.
    private const string ProductionPublicKeyBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE+n5+XJ3vE1lq/KqaHbbZOjlRww" +
        "nm+Fcq9fALr5Zuet0BJl9Y+udGorA5H8VgmQ47OOta73Na+FV8Q55sUf7ntg==";

    private readonly byte[] _publicKeyBytes;

    /// <summary>Production validator using the embedded public key.</summary>
    public LicenseValidator()
    {
        _publicKeyBytes = Convert.FromBase64String(ProductionPublicKeyBase64);
    }

    /// <summary>Test/KeyGen constructor accepting a custom public key bytes.</summary>
    public LicenseValidator(byte[] publicKeyBytes)
    {
        _publicKeyBytes = publicKeyBytes;
    }

    /// <summary>
    /// Validates a license key string. Key format: RAME-[base32 encoded payload+signature]
    /// Payload layout: version(1B) + machineIdHash(12B) + expiryDays(4B) + tier(1B) + nameLen(1B) + name(variable)
    /// Signature: ECDsa P-256 over SHA-256 of payload (64 bytes), appended after payload.
    /// </summary>
    public (LicenseActivationResult Result, LicenseInfo Info) Validate(
        string? keyString, byte[] machineIdHash, DateTime today)
    {
        if (string.IsNullOrWhiteSpace(keyString))
            return (LicenseActivationResult.Malformed, LicenseInfo.Unlicensed);

        try
        {
            var normalized = NormalizeKey(keyString);
            if (!normalized.StartsWith("RAME", StringComparison.OrdinalIgnoreCase))
                return (LicenseActivationResult.Malformed, LicenseInfo.Unlicensed);

            var base32Part = normalized[4..].Replace("-", "");
            byte[] combined;
            try
            {
                combined = Base32Decode(base32Part);
            }
            catch
            {
                return (LicenseActivationResult.Malformed, LicenseInfo.Unlicensed);
            }

            // Minimum: version(1) + machineId(12) + expiry(4) + tier(1) + nameLen(1) = 19 payload + 64 sig = 83
            if (combined.Length < 83)
                return (LicenseActivationResult.Malformed, LicenseInfo.Unlicensed);

            int payloadLen = combined.Length - 64;
            var payload = new byte[payloadLen];
            var signature = new byte[64];
            Array.Copy(combined, payload, payloadLen);
            Array.Copy(combined, payloadLen, signature, 0, 64);

            // Verify signature first
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(_publicKeyBytes, out _);
            if (!ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256))
                return (LicenseActivationResult.InvalidSignature, LicenseInfo.Unlicensed);

            // Parse payload
            int offset = 0;
            var majorVersion = payload[offset++];
            if (majorVersion != Constants.MajorVersion)
                return (LicenseActivationResult.WrongVersion, LicenseInfo.Unlicensed);

            if (offset + 12 > payloadLen)
                return (LicenseActivationResult.Malformed, LicenseInfo.Unlicensed);
            var keyMachineHash = new byte[12];
            Array.Copy(payload, offset, keyMachineHash, 0, 12);
            offset += 12;

            // Use first 12 bytes of provided hash — supports future longer hash formats
            var effectiveMachineHash = machineIdHash.Length >= 12
                ? machineIdHash.AsSpan(0, 12)
                : machineIdHash.AsSpan();
            if (!keyMachineHash.AsSpan().SequenceEqual(effectiveMachineHash))
                return (LicenseActivationResult.WrongMachine, LicenseInfo.Unlicensed);

            if (offset + 4 > payloadLen)
                return (LicenseActivationResult.Malformed, LicenseInfo.Unlicensed);
            var expiryDays = BitConverter.ToUInt32(payload, offset);
            offset += 4;

            if (offset + 1 > payloadLen)
                return (LicenseActivationResult.Malformed, LicenseInfo.Unlicensed);
            var tierByte = payload[offset++];
            if (!Enum.IsDefined(typeof(LicenseTier), tierByte))
                return (LicenseActivationResult.Malformed, LicenseInfo.Unlicensed);
            var tier = (LicenseTier)tierByte;

            if (offset + 1 > payloadLen)
                return (LicenseActivationResult.Malformed, LicenseInfo.Unlicensed);
            var nameLen = payload[offset++];
            if (offset + nameLen > payloadLen)
                return (LicenseActivationResult.Malformed, LicenseInfo.Unlicensed);
            var licenseeName = Encoding.UTF8.GetString(payload, offset, nameLen);

            DateTime? expiryDate = null;
            if (expiryDays > 0)
            {
                // Epoch: 2000-01-01 UTC
                expiryDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(expiryDays);
                if (today.Date > expiryDate.Value.Date)
                    return (LicenseActivationResult.Expired, LicenseInfo.Unlicensed);
            }

            var daysRemaining = expiryDate.HasValue
                ? Math.Max(0, (int)(expiryDate.Value.Date - today.Date).TotalDays)
                : (int?)null;
            var info = new LicenseInfo(true, keyString, licenseeName, expiryDate, tier, majorVersion, daysRemaining);
            return (LicenseActivationResult.Success, info);
        }
        catch
        {
            return (LicenseActivationResult.Malformed, LicenseInfo.Unlicensed);
        }
    }

    private static string NormalizeKey(string key)
        => key.Trim().ToUpperInvariant().Replace(" ", "").Replace("\n", "").Replace("\r", "");

    /// <summary>
    /// Returns the SHA-256 fingerprint of the embedded public key in colon-separated hex format
    /// (e.g. "AB:CD:EF:..."). Users can compare this against the fingerprint published in the README
    /// to verify the key has not been replaced.
    /// </summary>
    public static string GetPublicKeyFingerprint()
    {
        var keyBytes = Convert.FromBase64String(ProductionPublicKeyBase64);
        var hash = SHA256.HashData(keyBytes);
        return string.Join(":", hash.Take(16).Select(b => b.ToString("X2")));
    }

    private static byte[] Base32Decode(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var result = new List<byte>();
        int buffer = 0;
        int bitsLeft = 0;
        foreach (char c in base32.ToUpperInvariant())
        {
            if (c == '=')
                break;
            var charIndex = alphabet.IndexOf(c);
            if (charIndex < 0)
                throw new FormatException($"Invalid base32 character: {c}");
            buffer <<= 5;
            buffer |= charIndex & 0x1F;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                result.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
            }
        }

        return result.ToArray();
    }
}