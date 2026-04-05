using System.Diagnostics;
using RunFence.Core;
using RunFence.Launch;
using RunFence.Launch.Tokens;

namespace RunFence.DragBridge;

public class DragBridgeLauncher(
    IAppLaunchOrchestrator launchOrchestrator,
    ISplitTokenLauncher splitTokenLauncher,
    ICurrentAccountLauncher currentAccountLauncher,
    ILoggingService log) : IDragBridgeLauncher
{
    public Process? LaunchDirect(string exePath, IReadOnlyList<string> args,
        bool useSplitToken = false, bool useLowIntegrity = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        try
        {
            if (useSplitToken)
            {
                var pid = splitTokenLauncher.Launch(psi, null, null, null, useLowIntegrity, LaunchTokenSource.CurrentProcess);
                try
                {
                    return Process.GetProcessById(pid);
                }
                catch
                {
                    return null;
                }
            }

            var currentPid = currentAccountLauncher.Launch(psi);
            try
            {
                return Process.GetProcessById(currentPid);
            }
            catch
            {
                return null;
            }
        }
        catch (Exception ex)
        {
            log.Error($"DragBridgeLauncher: direct launch failed for '{exePath}'", ex);
            return null;
        }
    }

    public int LaunchManaged(string exePath, string accountSid, IReadOnlyList<string> args)
    {
        return launchOrchestrator.LaunchExeReturnPid(exePath, accountSid, args.ToList());
    }

    public int LaunchDeElevated(string exePath, IReadOnlyList<string> args, bool skipSplitToken = false)
    {
        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        if (interactiveSid == null)
        {
            log.Error($"DragBridgeLauncher: No interactive user found — cannot launch de-elevated '{exePath}'");
            return 0;
        }
        try
        {
            return launchOrchestrator.LaunchExeReturnPid(exePath, interactiveSid, args.ToList(),
                useSplitToken: skipSplitToken ? false : null);
        }
        catch (Exception ex)
        {
            log.Error($"DragBridgeLauncher: de-elevated launch failed for '{exePath}'", ex);
            return 0;
        }
    }
}