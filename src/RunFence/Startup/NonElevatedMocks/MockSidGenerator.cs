using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class MockSidGenerator
{
    private SecurityIdentifier? _machineSid;

    public string DeriveFakeSid(string name, uint ridBase)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty.", nameof(name));

        var machineSid = GetMachineSid();
        var normalizedName = name.Trim().ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedName));
        var rid = ridBase + BitConverter.ToUInt32(hash, 0) % 10000u;
        return machineSid != null
            ? $"{machineSid}-{rid}"
            : $"S-1-5-21-{BitConverter.ToUInt32(hash, 4)}-{BitConverter.ToUInt32(hash, 8)}-{BitConverter.ToUInt32(hash, 12)}-{rid}";
    }

    private SecurityIdentifier? GetMachineSid()
        => _machineSid ??= WindowsIdentity.GetCurrent().User?.AccountDomainSid;
}
