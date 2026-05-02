using System.Diagnostics;
using RunFence.Core;

namespace RunFence.Startup.UI;

public class UnlockProcessLauncher(ILoggingService log) : IUnlockProcessLauncher
{
    public void LaunchUnlockProcess(bool operationUnlock)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = operationUnlock ? PathConstants.UnlockOperationCmdPath : PathConstants.UnlockCmdPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            log.Error("Failed to launch unlock process", ex);
        }
    }
}
