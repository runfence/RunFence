using System.Diagnostics;
using RunFence.Core;

namespace RunFence.Launcher;

public class LauncherGuiController : ILauncherGuiController
{
    public bool IsGuiRunning()
    {
        try
        {
            using var mutex = Mutex.OpenExisting(IpcConstants.MutexName);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    public bool StartGui(bool grantStartupRunAsUnlock)
    {
        try
        {
            var guiPath = Path.Combine(AppContext.BaseDirectory, "RunFence.exe");
            if (!File.Exists(guiPath))
            {
                guiPath = Path.Combine(
                    Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)) ?? "",
                    "RunFence", "RunFence.exe");
            }

            if (!File.Exists(guiPath))
                return false;

            Process.Start(new ProcessStartInfo
            {
                FileName = guiPath,
                Arguments = grantStartupRunAsUnlock ? "--background --startup-runas" : "--background",
                UseShellExecute = true,
                Verb = "runas"
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
