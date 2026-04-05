using System.Diagnostics;
using System.Security;

namespace RunFence.Launch.Tokens;

public interface ISplitTokenLauncher
{
    int Launch(ProcessStartInfo psi, SecureString? password, string? domain, string? username,
        bool applyLowIl, LaunchTokenSource tokenSource = LaunchTokenSource.Credentials,
        Dictionary<string, string>? extraEnvVars = null, bool hideWindow = false);
}