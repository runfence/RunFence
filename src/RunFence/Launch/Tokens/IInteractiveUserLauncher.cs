using System.Diagnostics;

namespace RunFence.Launch.Tokens;

public interface IInteractiveUserLauncher
{
    /// <summary>Returns the PID of the launched process.</summary>
    int Launch(ProcessStartInfo psi, Dictionary<string, string>? extraEnvVars = null, bool hideWindow = false);
}