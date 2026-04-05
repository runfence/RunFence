using System.Runtime.InteropServices;

namespace RunFence.Acl;

internal static class CachingLocalUserProviderNative
{
    public const int NerrSuccess = 0;
    public const int ErrorMoreData = 234;
    public const int FilterNormalAccount = 0x0002;
    public const uint UFAccountDisable = 0x0002;

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

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserGetInfo(
        string? serverName,
        string userName,
        int level,
        out IntPtr bufPtr);

    [DllImport("netapi32.dll")]
    public static extern int NetApiBufferFree(IntPtr buffer);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ConvertSidToStringSidW(IntPtr pSid, out IntPtr stringSid);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LocalFree(IntPtr hMem);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct USER_INFO_23
    {
        public IntPtr usri23_name;
        public IntPtr usri23_full_name;
        public IntPtr usri23_comment;
        public uint usri23_flags;
        public IntPtr usri23_user_sid;
    }
}