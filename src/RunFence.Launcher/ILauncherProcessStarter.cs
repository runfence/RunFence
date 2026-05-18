using System.Diagnostics;

namespace RunFence.Launcher;

public interface ILauncherProcessStarter
{
    void Start(ProcessStartInfo startInfo);
}
