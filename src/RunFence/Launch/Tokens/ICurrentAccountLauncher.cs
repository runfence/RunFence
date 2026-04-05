using System.Diagnostics;

namespace RunFence.Launch.Tokens;

public interface ICurrentAccountLauncher
{
    int Launch(ProcessStartInfo psi, Dictionary<string, string>? extraEnvVars = null, bool hideWindow = false);
}