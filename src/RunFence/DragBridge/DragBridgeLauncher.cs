using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch;
using RunFence.Launch.Tokens;

namespace RunFence.DragBridge;

public class DragBridgeLauncher(
    ILaunchFacade facade,
    ILoggingService log) : IDragBridgeLauncher
{
    public ProcessInfo? LaunchDirect(string exePath, IReadOnlyList<string> args,
        PrivilegeLevel privilegeLevel = PrivilegeLevel.HighestAllowed)
    {
        var target = new ProcessLaunchTarget
        (
            ExePath: exePath,
            HideWindow: true,
            ArgumentsList: args.ToList()
        );
        try
        {
            return facade.LaunchFile(target, AccountLaunchIdentity.CurrentAccountBasic with { PrivilegeLevel = privilegeLevel });
        }
        catch (Exception ex)
        {
            log.Error($"DragBridgeLauncher: direct launch failed for '{exePath}'", ex);
            return null;
        }
    }

    public ProcessInfo LaunchManaged(string exePath, string accountSid, IReadOnlyList<string> args)
    {
        return facade.LaunchFile(new ProcessLaunchTarget(exePath, ArgumentsList: args.ToList()), new AccountLaunchIdentity(accountSid))
               ?? throw new InvalidOperationException($"LaunchManaged returned null process for '{exePath}'");
    }

    public ProcessInfo? LaunchDeElevated(string exePath, IReadOnlyList<string> args, PrivilegeLevel? privilegeLevel = null)
    {
        try
        {
            return facade.LaunchFile(new ProcessLaunchTarget(exePath, ArgumentsList: args.ToList()), AccountLaunchIdentity.InteractiveUser with { PrivilegeLevel = privilegeLevel });
        }
        catch (Exception ex)
        {
            log.Error($"DragBridgeLauncher: de-elevated launch failed for '{exePath}'", ex);
            return null;
        }
    }
}
