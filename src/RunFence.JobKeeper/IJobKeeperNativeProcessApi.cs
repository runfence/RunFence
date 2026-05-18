using System.Text;

namespace RunFence.JobKeeper;

public interface IJobKeeperNativeProcessApi
{
    bool CreateProcess(
        string? applicationName,
        StringBuilder commandLine,
        uint creationFlags,
        IntPtr environmentBlock,
        string? workingDirectory,
        bool hideWindow,
        bool suppressStartupFeedback,
        out JobKeeperProcessInformation processInformation);

    void CloseHandle(IntPtr handle);
    int GetLastWin32Error();
    bool TerminateProcess(IntPtr processHandle, uint exitCode);
    string? TryGetProcessImagePath(IntPtr processHandle);
    bool WaitForProcessExit(IntPtr processHandle, uint timeoutMilliseconds);
}
