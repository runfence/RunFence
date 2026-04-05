using System.Diagnostics;

namespace RunFence.DragBridge;

public interface IDragBridgeLauncher
{
    Process? LaunchDirect(string exePath, IReadOnlyList<string> args,
        bool useSplitToken = false, bool useLowIntegrity = false);

    /// <summary>Returns the PID of the launched process, or 0 on failure.</summary>
    int LaunchManaged(string exePath, string accountSid, IReadOnlyList<string> args);

    /// <summary>Returns the PID of the launched process, or 0 on failure.</summary>
    int LaunchDeElevated(string exePath, IReadOnlyList<string> args,
        bool skipSplitToken = false);
}