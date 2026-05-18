using System.Security.Cryptography;
using System.Text;
using RunFence.Core;

namespace RunFence.Licensing;

public class MachineIdProvider : IMachineIdProvider
{
    private static readonly HashSet<string> InvalidIdentityValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "",
        "00000000-0000-0000-0000-000000000000",
        "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF",
        "To be filled by O.E.M.",
        "Default string",
        "System Product Name",
        "System Serial Number",
        "None",
        "Unknown"
    };

    private readonly MachineIdentityResult _result;

    public MachineIdProvider(ILoggingService log)
        : this(new MachineIdentityReader(), log)
    {
    }

    public MachineIdProvider(IMachineIdentityReader reader, ILoggingService? log = null)
    {
        _result = Resolve(reader, log);
    }

    // Test constructor for explicit SMBIOS value-only tests.
    public MachineIdProvider(string? smbiosUuid, ILoggingService? log = null)
        : this(new FixedMachineIdentityReader(smbiosUuid, null), log)
    {
    }

    public MachineIdentityResult GetMachineIdentity()
        => _result with { MachineIdHash = _result.MachineIdHash is null ? null : (byte[])_result.MachineIdHash.Clone() };

    public string MachineCode => _result.Status == MachineIdentityStatus.Available
        ? _result.MachineCode!
        : throw new InvalidOperationException(_result.ErrorText ?? "Machine identity unavailable.");

    public byte[] MachineIdHash => _result.Status == MachineIdentityStatus.Available
        ? (byte[])_result.MachineIdHash!.Clone()
        : throw new InvalidOperationException(_result.ErrorText ?? "Machine identity unavailable.");

    private static MachineIdentityResult Resolve(IMachineIdentityReader reader, ILoggingService? log)
    {
        if (TryNormalizeIdentity(reader.ReadSmbiosUuid(), expectGuid: true, out var smbios))
            return BuildAvailable(MachineIdentitySource.SmbiosUuid, smbios);

        if (TryNormalizeIdentity(reader.ReadWindowsMachineGuid(), expectGuid: false, out var machineGuid))
            return BuildAvailable(MachineIdentitySource.WindowsMachineGuid, machineGuid);

        const string error = "Machine identity unavailable. SMBIOS UUID and Windows MachineGuid are missing or invalid.";
        log?.Warn(error);
        return new MachineIdentityResult(MachineIdentityStatus.Unavailable, null, null, null, null, error);
    }

    private static MachineIdentityResult BuildAvailable(MachineIdentitySource source, string canonicalValue)
    {
        var hash = ComputeHash(canonicalValue);
        return new MachineIdentityResult(
            MachineIdentityStatus.Available,
            source,
            canonicalValue,
            hash,
            FormatMachineCode(hash),
            null);
    }

    private static bool TryNormalizeIdentity(string? value, bool expectGuid, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (InvalidIdentityValues.Contains(trimmed))
            return false;

        if (expectGuid)
        {
            if (!Guid.TryParse(trimmed, out var parsed))
                return false;

            canonical = parsed.ToString("D").ToUpperInvariant();
            return !InvalidIdentityValues.Contains(canonical);
        }

        canonical = trimmed.ToUpperInvariant();
        return !InvalidIdentityValues.Contains(canonical);
    }

    public static byte[] ComputeHash(string rawValue)
    {
        var valueBytes = Encoding.UTF8.GetBytes(rawValue.ToUpperInvariant());
        var fullHash = SHA256.HashData(valueBytes);
        var result = new byte[12];
        Array.Copy(fullHash, result, 12);
        return result;
    }

    public static string FormatMachineCode(byte[] hash12)
        => FormatMachineCode(Base32Encode(hash12));

    private static string FormatMachineCode(string base32)
    {
        if (base32.Length > 20)
            base32 = base32[..20];

        var sb = new StringBuilder();
        for (var i = 0; i < base32.Length; i++)
        {
            if (i > 0 && i % 5 == 0)
                sb.Append('-');
            sb.Append(base32[i]);
        }

        return sb.ToString();
    }

    public static string Base32Encode(byte[] data) => Base32.Encode(data);

    private sealed class FixedMachineIdentityReader(string? smbiosUuid, string? machineGuid) : IMachineIdentityReader
    {
        public string? ReadSmbiosUuid() => smbiosUuid;
        public string? ReadWindowsMachineGuid() => machineGuid;
    }
}
