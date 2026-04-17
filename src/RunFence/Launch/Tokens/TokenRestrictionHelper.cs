using System.Runtime.InteropServices;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

public static class TokenRestrictionHelper
{
    private const int TokenElevation = 20;

    public static bool IsTokenElevated(IntPtr hToken)
    {
        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            if (ProcessNative.GetTokenInformation(hToken, TokenElevation, buffer, 4, out _))
                return Marshal.ReadInt32(buffer) != 0;
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
