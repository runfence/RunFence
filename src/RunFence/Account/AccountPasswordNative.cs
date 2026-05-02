using System.Runtime.InteropServices;

namespace RunFence.Account;

internal static class AccountPasswordNative
{
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserChangePassword(string? domainname, string username,
        IntPtr oldPassword, IntPtr newPassword);
}