using System.Diagnostics;
using System.Security;

namespace RunFence.Launch.Tokens;

public interface ILowIntegrityLauncher
{
    void Launch(ProcessStartInfo psi, SecureString? password, string? domain, string? username,
        LaunchTokenSource tokenSource = LaunchTokenSource.Credentials,
        Dictionary<string, string>? extraEnvVars = null, bool hideWindow = false);
}