using System.Runtime.InteropServices;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

public sealed class ProcessControl : IProcessControl
{
    public IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId) =>
        ProcessNative.OpenProcess(desiredAccess, inheritHandle, processId);

    public bool ResumeThread(IntPtr threadHandle, out int win32Error)
    {
        var result = ProcessLaunchNative.ResumeThread(threadHandle);
        win32Error = result == uint.MaxValue ? Marshal.GetLastWin32Error() : 0;
        return result != uint.MaxValue;
    }

    public bool TryTerminateProcess(IntPtr processHandle, uint exitCode, out int win32Error)
    {
        var result = ProcessLaunchNative.TerminateProcess(processHandle, exitCode);
        win32Error = result ? 0 : Marshal.GetLastWin32Error();
        return result;
    }

    public void TerminateProcessBestEffort(IntPtr processHandle, uint exitCode) =>
        _ = TryTerminateProcess(processHandle, exitCode, out _);

    public void CloseHandle(IntPtr handle) => ProcessNative.CloseHandle(handle);
}
