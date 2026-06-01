using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Tokens;

namespace RunFence.DragBridge;

public class DragBridgeLauncher(
    ILaunchFacade facade,
    IInteractiveUserSidResolver interactiveUserSidResolver,
    ILoggingService log) : IDragBridgeLauncher
{
    public ProcessInfo? LaunchDirect(string exePath, IReadOnlyList<string> args,
        PrivilegeLevel privilegeLevel = PrivilegeLevel.HighestAllowed)
    {
        var target = new ProcessLaunchTarget
        (
            ExePath: exePath,
            HideWindow: true,
            SuppressStartupFeedback: true,
            ArgumentsList: args.ToList()
        );
        try
        {
            using var launch = facade.LaunchFile(target, AccountLaunchIdentity.CurrentAccountIsolated with { PrivilegeLevel = privilegeLevel });
            LogMaintenanceWarnings("direct", exePath, launch);
            return launch.DetachProcess();
        }
        catch (Exception ex)
        {
            log.Error($"DragBridgeLauncher: direct launch failed for '{exePath}'", ex);
            return null;
        }
    }

    public ProcessInfo LaunchManaged(string exePath, string accountSid, IReadOnlyList<string> args,
        PrivilegeLevel privilegeLevel)
    {
        using var launch = facade.LaunchFile(
            new ProcessLaunchTarget(exePath, ArgumentsList: args.ToList(), SuppressStartupFeedback: true),
            new AccountLaunchIdentity(accountSid) { PrivilegeLevel = privilegeLevel });
        LogMaintenanceWarnings("managed", exePath, launch);
        return launch.DetachProcess()
               ?? throw new InvalidOperationException($"LaunchManaged returned no process for '{exePath}'");
    }

    public ProcessInfo LaunchAppContainer(string exePath, AppContainerEntry entry, IReadOnlyList<string> args)
    {
        using var launch = facade.LaunchFile(
            new ProcessLaunchTarget(exePath, ArgumentsList: args.ToList(), SuppressStartupFeedback: true),
            new AppContainerLaunchIdentity(entry));
        LogMaintenanceWarnings("appcontainer", exePath, launch);
        return launch.DetachProcess()
               ?? throw new InvalidOperationException($"LaunchAppContainer returned no process for '{exePath}'");
    }

    public ProcessInfo? LaunchDeElevated(string exePath, IReadOnlyList<string> args, PrivilegeLevel? privilegeLevel = null)
    {
        try
        {
            var interactiveSid = interactiveUserSidResolver.GetInteractiveUserSid();
            if (string.IsNullOrWhiteSpace(interactiveSid))
            {
                log.Error("DragBridgeLauncher: de-elevated launch failed because the interactive user is unavailable.",
                    new InvalidOperationException("Interactive user is unavailable (explorer not running)."));
                return null;
            }

            using var launch = facade.LaunchFile(
                new ProcessLaunchTarget(exePath, ArgumentsList: args.ToList(), SuppressStartupFeedback: true),
                new AccountLaunchIdentity(interactiveSid) { PrivilegeLevel = privilegeLevel });
            LogMaintenanceWarnings("de-elevated", exePath, launch);
            return launch.DetachProcess();
        }
        catch (Exception ex)
        {
            log.Error($"DragBridgeLauncher: de-elevated launch failed for '{exePath}'", ex);
            return null;
        }
    }

    private void LogMaintenanceWarnings(string mode, string exePath, LaunchExecutionResult launch)
    {
        var warning = LaunchExecutionWarningFormatter.Format("The Drag Bridge helper", launch);
        if (warning != null)
            log.Warn($"DragBridgeLauncher: {mode} launch started for '{exePath}' with warnings. {warning}");
    }
}
