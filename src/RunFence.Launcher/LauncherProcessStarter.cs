using System.Diagnostics;

namespace RunFence.Launcher;

public sealed class LauncherProcessStarter : ILauncherProcessStarter
{
    public void Start(ProcessStartInfo startInfo) => Process.Start(startInfo);
}
