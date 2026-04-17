using RunFence.Core.Models;
using RunFence.Launch.Tokens;

namespace RunFence.DragBridge;

public interface IDragBridgeLauncher
{
    ProcessInfo? LaunchDirect(string exePath, IReadOnlyList<string> args,
        PrivilegeLevel privilegeLevel = PrivilegeLevel.HighestAllowed);

    /// <summary>Returns the <see cref="ProcessInfo"/> of the launched process.</summary>
    ProcessInfo LaunchManaged(string exePath, string accountSid, IReadOnlyList<string> args);

    /// <summary>Returns the <see cref="ProcessInfo"/> of the launched process, or null on failure.</summary>
    ProcessInfo? LaunchDeElevated(string exePath, IReadOnlyList<string> args,
        PrivilegeLevel? privilegeLevel = null);
}
