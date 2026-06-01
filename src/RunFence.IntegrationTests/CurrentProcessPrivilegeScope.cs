using System.ComponentModel;
using System.Runtime.InteropServices;
using RunFence.Infrastructure;
using RunFence.Launch.Tokens;

namespace RunFence.IntegrationTests;

internal sealed class CurrentProcessPrivilegeScope : IDisposable
{
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const int TokenPrivilegesClass = 3;
    private const uint SePrivilegeEnabled = 0x00000002;
    private const int LuidAttributeRecordSize = 12;

    private readonly IntPtr tokenHandle;
    private readonly string privilegeName;
    private readonly bool restoreOnDispose;
    private readonly bool privilegeWasEnabled;

    public CurrentProcessPrivilegeScope(string privilegeName, bool restoreOnDispose = true)
    {
        this.privilegeName = privilegeName;
        this.restoreOnDispose = restoreOnDispose;
        if (!ProcessNative.OpenProcessToken(ProcessNative.GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out tokenHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        privilegeWasEnabled = IsPrivilegeEnabled(tokenHandle, privilegeName);
        TokenPrivilegeHelper.DisablePrivilegesOnToken(tokenHandle, [privilegeName]);
    }

    public void Dispose()
    {
        try
        {
            if (!restoreOnDispose)
                return;

            if (privilegeWasEnabled)
                TokenPrivilegeHelper.EnablePrivilegeOnToken(tokenHandle, privilegeName);
            else
                TokenPrivilegeHelper.DisablePrivilegesOnToken(tokenHandle, [privilegeName]);
        }
        finally
        {
            if (tokenHandle != IntPtr.Zero)
                ProcessNative.CloseHandle(tokenHandle);
        }
    }

    private static bool IsPrivilegeEnabled(IntPtr tokenHandle, string privilegeName)
    {
        if (!TokenPrivilegeNative.LookupPrivilegeValue(null, privilegeName, out var targetLuid))
            return false;

        ProcessNative.GetTokenInformation(tokenHandle, TokenPrivilegesClass, IntPtr.Zero, 0, out var required);
        if (required == 0)
            return false;

        var buffer = Marshal.AllocHGlobal((int)required);
        try
        {
            if (!ProcessNative.GetTokenInformation(tokenHandle, TokenPrivilegesClass, buffer, required, out _))
                return false;

            var count = Marshal.ReadInt32(buffer);
            for (var i = 0; i < count; i++)
            {
                var baseOffset = 4 + i * LuidAttributeRecordSize;
                var lowPart = (uint)Marshal.ReadInt32(buffer, baseOffset);
                var highPart = Marshal.ReadInt32(buffer, baseOffset + 4);
                if (lowPart != targetLuid.LowPart || highPart != targetLuid.HighPart)
                    continue;

                var attributes = (uint)Marshal.ReadInt32(buffer, baseOffset + 8);
                return (attributes & SePrivilegeEnabled) != 0;
            }

            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

