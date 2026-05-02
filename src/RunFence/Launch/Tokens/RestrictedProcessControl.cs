using System.Runtime.InteropServices;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

public sealed class RestrictedProcessControl : IRestrictedProcessControl
{
    public bool ResumeThread(IntPtr threadHandle, out int win32Error)
    {
        var result = ProcessLaunchNative.ResumeThread(threadHandle);
        win32Error = result == uint.MaxValue ? Marshal.GetLastWin32Error() : 0;
        return result != uint.MaxValue;
    }

    public void TerminateProcess(IntPtr processHandle)
    {
        try { ProcessLaunchNative.TerminateProcess(processHandle, 1); } catch { }
    }

    public void CloseHandle(IntPtr handle) => ProcessNative.CloseHandle(handle);
}
