using System.Runtime.InteropServices;

namespace RunFence.Account;

internal static class WindowsAccountNative
{
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool LogonUser(string lpszUsername, string? lpszDomain,
        string lpszPassword, int dwLogonType, int dwLogonProvider, out IntPtr phToken);


    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct USER_INFO_0
    {
        public string usri0_name;
    }

    [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool DeleteProfile(string pszSidString, string? pszProfilePath, IntPtr pReserved);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserSetInfo(string? servername, string username, uint level, ref USER_INFO_0 buf, out uint parmErr);
}