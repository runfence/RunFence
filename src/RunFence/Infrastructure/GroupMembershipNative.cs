using System.Runtime.InteropServices;

namespace RunFence.Infrastructure;

internal static class GroupMembershipNative
{
    public const int LgIncludeIndirect = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct LOCALGROUP_USERS_INFO_0
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lgrui0_name;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct LOCALGROUP_INFO_1
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lgrpi1_name;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lgrpi1_comment;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct LOCALGROUP_INFO_1002
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lgrpi1002_comment;
    }

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserGetLocalGroups(string? servername, string username,
        int level, int flags, out IntPtr bufptr, int prefmaxlen,
        out int entriesread, out int totalentries);

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetLocalGroupGetInfo(string? servername, string groupname,
        int level, out IntPtr bufptr);

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetLocalGroupSetInfo(string? servername, string groupname,
        int level, IntPtr buf, out int parm_err);

    [DllImport("Netapi32.dll")]
    public static extern int NetApiBufferFree(IntPtr buffer);
}
