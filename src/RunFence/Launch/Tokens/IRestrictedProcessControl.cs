namespace RunFence.Launch.Tokens;

public interface IRestrictedProcessControl
{
    bool ResumeThread(IntPtr threadHandle, out int win32Error);
    void TerminateProcess(IntPtr processHandle);
    void CloseHandle(IntPtr handle);
}
