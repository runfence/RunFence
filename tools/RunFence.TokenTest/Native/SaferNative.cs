using System.Runtime.InteropServices;

namespace RunFence.TokenTest.Native;

internal static class SaferNative
{
    public const uint SAFER_SCOPEID_MACHINE = 1;
    public const uint SAFER_LEVELID_NORMALUSER = 0x00020000;
    public const uint SAFER_LEVEL_OPEN = 1;

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool SaferCreateLevel(
        uint ScopeId,
        uint LevelId,
        uint OpenFlags,
        out IntPtr LevelHandle,
        IntPtr lpReserved);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool SaferComputeTokenFromLevel(
        IntPtr LevelHandle,
        IntPtr InAccessToken,
        out IntPtr OutAccessToken,
        uint dwFlags,
        IntPtr lpReserved);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool SaferCloseLevel(IntPtr hLevelHandle);
}
