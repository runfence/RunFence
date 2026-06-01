namespace RunFence.Launch.Tokens;

public interface IProcessControl
{
    IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);
    bool ResumeThread(IntPtr threadHandle, out int win32Error);
    bool TryTerminateProcess(IntPtr processHandle, uint exitCode, out int win32Error);
    void TerminateProcessBestEffort(IntPtr processHandle, uint exitCode);
    void CloseHandle(IntPtr handle);
}
