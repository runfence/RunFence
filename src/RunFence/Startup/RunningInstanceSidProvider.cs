using System.Diagnostics;
using RunFence.Core;

namespace RunFence.Startup;

/// <summary>
/// Finds another running instance of RunFence and returns its process owner SID and session ID.
/// </summary>
public class RunningInstanceSidProvider : IRunningInstanceSidProvider
{
    public RunningInstanceInfo? GetRunningInstanceInfo()
    {
        var currentPid = Environment.ProcessId;
        var candidates = Process.GetProcessesByName("RunFence");
        try
        {
            var other = candidates.FirstOrDefault(p => p.Id != currentPid);
            if (other == null)
                return null;
            var sid = NativeTokenHelper.TryGetProcessOwnerSid((uint)other.Id)?.Value;
            if (sid == null)
                return null;
            return new RunningInstanceInfo(sid, other.SessionId);
        }
        finally
        {
            foreach (var p in candidates) p.Dispose();
        }
    }
}
