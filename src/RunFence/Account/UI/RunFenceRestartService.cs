using System.Diagnostics;
using RunFence.Core;
using RunFence.Launch;

namespace RunFence.Account.UI;

public sealed class RunFenceRestartService : IRunFenceRestartService
{
    public void Restart()
    {
        var runFenceExePath = Path.Combine(AppContext.BaseDirectory, "RunFence.exe");
        if (!PathHelper.IsPathSafeForCmd(runFenceExePath))
            throw new InvalidOperationException("RunFence executable path contains characters unsafe for cmd.exe restart.");

        var cmdExePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var restartCommand = $"ping 127.0.0.1 -n 3 >nul & start \"\" \"{runFenceExePath}\"";
        var startInfo = new ProcessStartInfo(cmdExePath)
        {
            Arguments = "/d /c " + restartCommand,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        if (Process.Start(startInfo) == null)
            throw new InvalidOperationException("Failed to start delayed RunFence restart command.");
        Application.Exit();
    }
}
