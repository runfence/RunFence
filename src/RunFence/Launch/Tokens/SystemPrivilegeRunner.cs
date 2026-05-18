using System.ComponentModel;
using System.Runtime.InteropServices;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

public class SystemPrivilegeRunner(ISystemTokenProvider systemTokenProvider) : ISystemPrivilegeRunner
{
    public void RunWithPrivileges(IEnumerable<string> privilegeNames, Action action)
        => RunWithPrivileges(privilegeNames, () =>
        {
            action();
            return true;
        });

    public T RunWithPrivileges<T>(IEnumerable<string> privilegeNames, Func<T> action)
    {
        IntPtr hSystemPrimary = IntPtr.Zero;
        IntPtr hSystemImpersonation = IntPtr.Zero;
        try
        {
            hSystemPrimary = systemTokenProvider.AcquireSystemToken();

            if (!ProcessLaunchNative.DuplicateTokenEx(
                    hSystemPrimary,
                    ProcessLaunchNative.MAXIMUM_ALLOWED,
                    IntPtr.Zero,
                    (int)ProcessLaunchNative.SecurityImpersonationLevel.SecurityImpersonation,
                    (int)ProcessLaunchNative.TokenType.TokenImpersonation,
                    out hSystemImpersonation))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx(SYSTEM impersonation) failed");
            }

            foreach (var privilegeName in privilegeNames)
                TokenPrivilegeHelper.EnablePrivilegeOnToken(hSystemImpersonation, privilegeName);

            if (!ProcessNative.ImpersonateLoggedOnUser(hSystemImpersonation))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ImpersonateLoggedOnUser(SYSTEM) failed");

            try
            {
                return action();
            }
            finally
            {
                ProcessNative.RevertToSelf();
            }
        }
        finally
        {
            if (hSystemImpersonation != IntPtr.Zero)
                ProcessNative.CloseHandle(hSystemImpersonation);
            if (hSystemPrimary != IntPtr.Zero)
                ProcessNative.CloseHandle(hSystemPrimary);
        }
    }
}
