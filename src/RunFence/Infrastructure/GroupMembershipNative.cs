using System.Runtime.InteropServices;

namespace RunFence.Infrastructure;

public static class GroupMembershipNative
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

    public const int UF_ACCOUNTDISABLE = 0x0002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct USER_INFO_1
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string usri1_name;
        [MarshalAs(UnmanagedType.LPWStr)] public string? usri1_password;
        public int usri1_password_age;
        public int usri1_priv;
        [MarshalAs(UnmanagedType.LPWStr)] public string? usri1_home_dir;
        [MarshalAs(UnmanagedType.LPWStr)] public string? usri1_comment;
        public int usri1_flags;
        [MarshalAs(UnmanagedType.LPWStr)] public string? usri1_script_path;
    }

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserGetInfo(string? serverName, string userName, int level, out IntPtr bufPtr);

    [DllImport("Netapi32.dll")]
    public static extern int NetApiBufferFree(IntPtr buffer);
}
