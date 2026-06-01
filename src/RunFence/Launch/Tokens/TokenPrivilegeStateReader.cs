using System.Runtime.InteropServices;
using System.Security.Principal;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

public sealed class TokenPrivilegeStateReader : ITokenPrivilegeStateReader
{
    private const int TokenElevation = 20;

    public bool IsElevated(IntPtr hToken)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            return ProcessNative.GetTokenInformation(hToken, TokenElevation, buffer, sizeof(int), out _)
                   && Marshal.ReadInt32(buffer) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public bool TryGetIntegrityLevel(IntPtr hToken, out int integrityLevel)
    {
        integrityLevel = 0;
        ProcessNative.GetTokenInformation(hToken, ProcessLaunchNative.TOKEN_INTEGRITY_LEVEL, IntPtr.Zero, 0, out var needed);
        if (needed == 0)
            return false;

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!ProcessNative.GetTokenInformation(hToken, ProcessLaunchNative.TOKEN_INTEGRITY_LEVEL, buffer, needed, out _))
                return false;

            var sidPtr = Marshal.ReadIntPtr(buffer);
            if (sidPtr == IntPtr.Zero)
                return false;

            SecurityIdentifier sid;
            try
            {
                sid = new SecurityIdentifier(sidPtr);
            }
            catch (ArgumentException)
            {
                return false;
            }

            var binary = new byte[sid.BinaryLength];
            sid.GetBinaryForm(binary, 0);
            integrityLevel = binary[^4]
                             | (binary[^3] << 8)
                             | (binary[^2] << 16)
                             | (binary[^1] << 24);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
