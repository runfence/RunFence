using System.Diagnostics;
using RunFence.Core;

namespace RunFence.Launcher;

public class LauncherGuiController : ILauncherGuiController
{
    public LauncherGuiInstanceState GetGuiState()
    {
        try
        {
            using var mutex = Mutex.OpenExisting(IpcConstants.MutexName);
            return IsRunFenceRunningInCurrentSession()
                ? LauncherGuiInstanceState.RunningInCurrentSession
                : LauncherGuiInstanceState.RunningInDifferentSession;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return LauncherGuiInstanceState.NotRunning;
        }
        catch (UnauthorizedAccessException)
        {
            return IsRunFenceRunningInCurrentSession()
                ? LauncherGuiInstanceState.RunningInCurrentSession
                : LauncherGuiInstanceState.RunningInDifferentSession;
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

    protected virtual int GetCurrentSessionId() => Process.GetCurrentProcess().SessionId;

    protected virtual IReadOnlyList<Process> GetRunFenceProcesses() => Process.GetProcessesByName("RunFence");

    private bool IsRunFenceRunningInCurrentSession()
    {
        var processes = GetRunFenceProcesses();
        try
        {
            var currentSessionId = GetCurrentSessionId();
            foreach (var process in processes)
            {
                try
                {
                    if (process.SessionId == currentSessionId)
                        return true;
                }
                catch
                {
                }
            }
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }

        return false;
    }
}
