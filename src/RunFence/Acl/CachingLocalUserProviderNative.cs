using System.Runtime.InteropServices;

namespace RunFence.Acl;

internal static class CachingLocalUserProviderNative
{
    public const int NerrSuccess = 0;
    public const int ErrorMoreData = 234;
    public const int FilterNormalAccount = 0x0002;

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserEnum(
        string? serverName,
        int level,
        int filter,
        out IntPtr bufPtr,
        int prefMaxLen,
        out int entriesRead,
        out int totalEntries,
        ref int resumeHandle);
}
