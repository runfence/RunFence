using RunFence.Core.Models;
using RunFence.Launch;
using RunFence.Persistence;

namespace RunFence.Account.UI;

public sealed class TerminalLaunchIdentitySelector(
    IDatabaseProvider databaseProvider,
    WindowsTerminalDeploymentPaths deploymentPaths)
{
    public LaunchIdentity ResolveLaunchIdentity(LaunchIdentity identity, string terminalExePath)
    {
        if (IsCmd(terminalExePath) ||
            IsSharedWindowsTerminal(terminalExePath) ||
            identity is not AccountLaunchIdentity { PrivilegeLevel: null } accountIdentity)
        {
            return identity;
        }

        var storedPrivilegeLevel = databaseProvider.GetDatabase().GetAccount(accountIdentity.Sid)?.PrivilegeLevel;
        return storedPrivilegeLevel == PrivilegeLevel.HighIntegrity
            ? identity
            : accountIdentity with { PrivilegeLevel = PrivilegeLevel.Basic };
    }

    private static bool IsCmd(string terminalExePath)
        => string.Equals(Path.GetFileName(terminalExePath), "cmd.exe", StringComparison.OrdinalIgnoreCase);

    private bool IsSharedWindowsTerminal(string terminalExePath)
        => deploymentPaths.IsSharedExecutablePath(terminalExePath);
}
