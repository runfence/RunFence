using System.Runtime.InteropServices;

namespace RunFence.Account;

internal static class WindowsAccountNative
{
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool LogonUser(string lpszUsername, string? lpszDomain,
        IntPtr lpszPassword, int dwLogonType, int dwLogonProvider, out IntPtr phToken);


    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct USER_INFO_0
    {
        public string usri0_name;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct USER_INFO_1_LAYOUT
    {
        public IntPtr usri1_name;
        public IntPtr usri1_password;
        public int usri1_password_age;
        public int usri1_priv;
        public IntPtr usri1_home_dir;
        public IntPtr usri1_comment;
        public int usri1_flags;
        public IntPtr usri1_script_path;
    }

    public const uint UF_NORMAL_ACCOUNT = 0x200u;
    public const uint UF_DONT_EXPIRE_PASSWD = 0x10000u;
    public const uint USER_PRIV_USER = 1u;

    [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool DeleteProfile(string pszSidString, string? pszProfilePath, IntPtr pReserved);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserSetInfo(string? servername, string username, uint level, ref USER_INFO_0 buf, out uint parmErr);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserSetInfo(string? servername, string username, uint level, IntPtr buf, out uint parmErr);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserAdd(string? servername, uint level, IntPtr buf, out uint parm_err);
}