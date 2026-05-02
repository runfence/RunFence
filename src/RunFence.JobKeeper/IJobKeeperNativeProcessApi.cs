using System.Text;

namespace RunFence.JobKeeper;

public interface IJobKeeperNativeProcessApi
{
    bool CreateProcess(
        StringBuilder commandLine,
        uint creationFlags,
        IntPtr environmentBlock,
        string? workingDirectory,
        bool hideWindow,
        out JobKeeperProcessInformation processInformation);

    void CloseHandle(IntPtr handle);
    int GetLastWin32Error();
}
