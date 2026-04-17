using System.Security.Cryptography;
using RunFence.Licensing;

namespace RunFence.Tests;

/// <summary>
/// Shared ECDsa key fixture for license tests.
/// Both <c>LicenseServiceTests</c> and <c>LicenseValidatorTests</c> use the same signing key bytes so
/// that keys built by one test class can be validated by the other without key-mismatch errors.
/// </summary>
internal static class LicenseTestKey
{
    private static readonly byte[] _privateKeyBytes;
    public static readonly byte[] PublicKeyBytes;

    static LicenseTestKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _privateKeyBytes = key.ExportECPrivateKey();
        PublicKeyBytes = key.ExportSubjectPublicKeyInfo();
    }

    /// <summary>Creates a new ECDsa instance from shared key bytes. Caller must dispose.</summary>
    public static ECDsa CreateSigningKey()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        key.ImportECPrivateKey(_privateKeyBytes, out _);
        return key;
    }
}
