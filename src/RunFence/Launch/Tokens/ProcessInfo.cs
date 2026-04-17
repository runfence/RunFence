using System.ComponentModel;
using System.Runtime.InteropServices;
using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

public sealed class ProcessInfo(ProcessLaunchNative.PROCESS_INFORMATION NativeInfo) : IDisposable
{
    public int Id => (int)NativeInfo.dwProcessId;

    public int ExitCode
    {
        get
        {
            if (NativeInfo.hProcess == IntPtr.Zero)
                return -1;

            if (!ProcessLaunchNative.GetExitCodeProcess(NativeInfo.hProcess, out var exitCode))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return (int)exitCode;
        }
    }

    public bool HasExited
    {
        get
        {
            if (NativeInfo.hProcess == IntPtr.Zero)
                return true;

            return ProcessLaunchNative.WaitForSingleObject(NativeInfo.hProcess, 0) == ProcessLaunchNative.WAIT_OBJECT_0;
        }
    }

    public void Kill(int exitCode = -1)
    {
        if (!ProcessLaunchNative.TerminateProcess(NativeInfo.hProcess, (uint)exitCode))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public bool WaitForExit(int timeoutMs)
    {
        if (NativeInfo.hProcess == IntPtr.Zero)
            return true;
        return ProcessLaunchNative.WaitForSingleObject(NativeInfo.hProcess, (uint)timeoutMs) == ProcessLaunchNative.WAIT_OBJECT_0;
    }


    private int _disposed;

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;
        if (NativeInfo.hProcess != IntPtr.Zero)
            ProcessNative.CloseHandle(NativeInfo.hProcess);
        if (NativeInfo.hThread != IntPtr.Zero)
            ProcessNative.CloseHandle(NativeInfo.hThread);
        GC.SuppressFinalize(this);
    }

    ~ProcessInfo()
    {
        Dispose();
    }
}