using System.ComponentModel;
using RunFence.Infrastructure;
using RunFence.Launch.Tokens;

namespace RunFence.IntegrationTests;

internal sealed class CurrentProcessPrivilegeScope : IDisposable
{
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private readonly IntPtr tokenHandle;
    private readonly string privilegeName;
    private readonly bool restoreEnabled;

    public CurrentProcessPrivilegeScope(string privilegeName, bool enableOnDispose)
    {
        this.privilegeName = privilegeName;
        restoreEnabled = enableOnDispose;
        if (!ProcessNative.OpenProcessToken(ProcessNative.GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out tokenHandle))
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());

        TokenPrivilegeHelper.DisablePrivilegesOnToken(tokenHandle, [privilegeName]);
    }

    public void Dispose()
    {
        try
        {
            if (restoreEnabled)
            {
                TokenPrivilegeHelper.EnablePrivilegeOnToken(tokenHandle, privilegeName);
            }
        }
        finally
        {
            if (tokenHandle != IntPtr.Zero)
                ProcessNative.CloseHandle(tokenHandle);
        }
    }
}
