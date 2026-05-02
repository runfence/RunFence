#pragma warning disable CS9113 // Parameter 'real' is intentionally unread — RegisterDecorator constructs the real implementation for DI validation
using System.Security.Principal;
using RunFence.Account;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class NoOpLsaRightsHelper(ILsaRightsHelper real) : ILsaRightsHelper
{
    // real is injected but unused — RegisterDecorator constructs the real implementation
    // to keep DI validation working; all calls are no-ops in non-elevated debug mode

    public byte[] GetSidBytes(string sidString)
    {
        var sid = new SecurityIdentifier(sidString);
        var bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        return bytes;
    }

    public byte[]? TryResolveSidBytes(string? domain, string username) => null;
    public List<string> EnumerateAccountRights(byte[] sidBytes) => [];
    public void AddAccountRights(byte[] sidBytes, string[] rights) { }
    public void RemoveAccountRights(byte[] sidBytes, string[] rights) { }
}
