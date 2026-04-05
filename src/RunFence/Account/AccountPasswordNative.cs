using System.Runtime.InteropServices;

namespace RunFence.Account;

internal static class AccountPasswordNative
{
    // oldPassword declared as IntPtr so the unmanaged SecureString pointer is passed directly —
    // no managed string copy is created for the old password.
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserChangePassword(string? domainname, string username,
        IntPtr oldPassword, string newPassword);
}