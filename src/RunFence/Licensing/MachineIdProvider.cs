using System.Management;
using System.Security.Cryptography;
using System.Text;
using RunFence.Core;

namespace RunFence.Licensing;

internal interface IMachineIdProvider
{
    string MachineCode { get; }
    byte[] MachineIdHash { get; }
}

internal class MachineIdProvider : IMachineIdProvider
{
    // SMBIOS UUID spoofing is an accepted risk for this offline-only validation scheme.
    // A determined user can spoof the UUID; we accept this tradeoff for usability.
    private readonly byte[] _machineIdHash;

    public MachineIdProvider(ILoggingService log) : this(GetSmbiosUuid(), log)
    {
    }

    public MachineIdProvider(string uuidString, ILoggingService? log = null)
    {
        _machineIdHash = ComputeHash(uuidString);
        var rawBase32 = Base32Encode(_machineIdHash);
        if (rawBase32.Length > 20)
        {
            log?.Warn($"Machine code base32 truncated to 20 chars. Full original: {rawBase32}");
            rawBase32 = rawBase32[..20];
        }

        MachineCode = FormatMachineCode(rawBase32);
    }

    public string MachineCode { get; }

    public byte[] MachineIdHash => (byte[])_machineIdHash.Clone();

    private static string GetSmbiosUuid()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                var uuid = obj["UUID"]?.ToString();
                if (!string.IsNullOrEmpty(uuid) && uuid != "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF")
                    return uuid;
            }
        }
        catch
        {
        }

        return Environment.MachineName;
    }

    public static byte[] ComputeHash(string uuidString)
    {
        var uuidBytes = Encoding.UTF8.GetBytes(uuidString.ToUpperInvariant());
        var fullHash = SHA256.HashData(uuidBytes);
        var result = new byte[12];
        Array.Copy(fullHash, result, 12);
        return result;
    }

    public static string FormatMachineCode(byte[] hash12)
        => FormatMachineCode(Base32Encode(hash12));

    private static string FormatMachineCode(string base32)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < base32.Length; i++)
        {
            if (i > 0 && i % 5 == 0)
                sb.Append('-');
            sb.Append(base32[i]);
        }

        return sb.ToString();
    }

    public static string Base32Encode(byte[] data) => Base32.Encode(data);
}