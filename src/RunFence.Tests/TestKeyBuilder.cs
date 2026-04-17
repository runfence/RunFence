using System.Security.Cryptography;
using System.Text;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Licensing;

namespace RunFence.Tests;

internal static class TestKeyBuilder
{
    public static string BuildKey(
        ECDsa signingKey,
        byte[] machineHash,
        byte? version = null,
        uint expiryDays = 0,
        LicenseTier tier = LicenseTier.Annual,
        string licenseeName = "Test User")
    {
        var actualVersion = version ?? Constants.MajorVersion;
        var nameBytes = Encoding.UTF8.GetBytes(licenseeName);

        var payload = new List<byte> { actualVersion };
        payload.AddRange(machineHash);
        payload.AddRange(BitConverter.GetBytes(expiryDays));
        payload.Add((byte)tier);
        payload.Add((byte)nameBytes.Length);
        payload.AddRange(nameBytes);

        var payloadBytes = payload.ToArray();
        var signature = signingKey.SignData(payloadBytes, HashAlgorithmName.SHA256);
        var combined = payloadBytes.Concat(signature).ToArray();
        var base32 = MachineIdProvider.Base32Encode(combined);

        var sb = new StringBuilder("RAME");
        for (int i = 0; i < base32.Length; i++)
        {
            if (i > 0 && i % 5 == 0)
                sb.Append('-');
            sb.Append(base32[i]);
        }

        return sb.ToString();
    }
}
