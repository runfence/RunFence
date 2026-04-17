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

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ConvertSidToStringSidW(IntPtr pSid, out IntPtr stringSid);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct USER_INFO_23
    {
        public IntPtr usri23_name;
        public IntPtr usri23_full_name;
        public IntPtr usri23_comment;
        public uint usri23_flags;
        public IntPtr usri23_user_sid;
    }
}