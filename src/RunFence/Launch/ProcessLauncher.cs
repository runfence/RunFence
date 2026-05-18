using System.ComponentModel;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch.Container;
using RunFence.Launch.Tokens;
using RunFence.Launching.Resolution;
using IProfileRepairHelper = RunFence.Account.IProfileRepairHelper;

namespace RunFence.Launch;

public class ProcessLauncher(
    IAccountProcessLauncher accountProcessLauncher,
    ILaunchCredentialsLookup credentialsLookup,
    IProfileRepairHelper profileRepairHelper,
    IAppContainerProcessLauncher appContainerLauncher,
    IWindowsAppsAliasPathResolver windowsAppsAliasPathResolver,
    IExecutableKindService executableKindService,
    IWindowsAppsRegistrationRepairRunner windowsAppsRegistrationRepairRunner,
    ILoggingService log)
    : IProcessLauncher, ILaunchIdentityAcceptor<ProcessInfo?>
{
    private const int ErrorAccessDenied = 5;

    public ProcessInfo? Launch(LaunchIdentity identity, ProcessLaunchTarget target)
        => identity.Visit(this, target);

    public ProcessInfo Accept(AccountLaunchIdentity identity, ProcessLaunchTarget? target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var creds = identity.Credentials ?? credentialsLookup.GetBySid(identity.Sid);
        try
        {
            var resolved = identity with { Credentials = creds };
            var launchTarget = ApplyWindowsAppsStartupFeedback(target, resolved.Sid);
            var r = LaunchAccountTargetWithRepair(launchTarget, identity, resolved);
            log.Info($"Launched {target.ExePath} as {creds.Username ?? (creds.TokenSource == LaunchTokenSource.InteractiveUser ? "interactive user" : "current account")}");
            return r;
        }
        finally
        {
            if (identity.Credentials == null)
                creds.Password?.Dispose();
        }
    }

    public ProcessInfo Accept(AppContainerLaunchIdentity identity, ProcessLaunchTarget? target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return appContainerLauncher.LaunchFile(target, identity);
    }

    private ProcessInfo LaunchAccountTargetWithRepair(
        ProcessLaunchTarget target,
        AccountLaunchIdentity originalIdentity,
        AccountLaunchIdentity resolvedIdentity)
    {
        try
        {
            return profileRepairHelper.ExecuteWithProfileRepair(
                () => accountProcessLauncher.Launch(target, resolvedIdentity),
                originalIdentity.Sid);
        }
        catch (Exception ex) when (IsAccessDeniedLaunchFailure(ex))
        {
            if (!windowsAppsRegistrationRepairRunner.TryRepair(target, originalIdentity, resolvedIdentity))
                throw;

            return profileRepairHelper.ExecuteWithProfileRepair(
                () => accountProcessLauncher.Launch(target, resolvedIdentity),
                originalIdentity.Sid);
        }
    }

    private ProcessLaunchTarget ApplyWindowsAppsStartupFeedback(ProcessLaunchTarget target, string targetUserSid)
    {
        if (target.SuppressStartupFeedback)
            return target;

        return IsWindowsAppsBackedTarget(target.ExePath, targetUserSid)
            ? target with { SuppressStartupFeedback = true }
            : target;
    }

    private bool IsWindowsAppsBackedTarget(string exePath, string targetUserSid)
    {
        if (WindowsAppsPackagePathParser.TryParsePackagePath(exePath, out _))
            return true;

        if (windowsAppsAliasPathResolver.IsWindowsAppsAliasPath(exePath))
            return true;

        if (!Path.IsPathRooted(exePath)
            && windowsAppsAliasPathResolver.TryResolveForUserSid(exePath, targetUserSid) != null)
        {
            return true;
        }

        try
        {
            var fullPath = Path.GetFullPath(exePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullPath.StartsWith(
                       windowsDirectory + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase)
                   && executableKindService.IsUwpExeFile(exePath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsAccessDeniedLaunchFailure(Exception ex) =>
        ex is Win32Exception { NativeErrorCode: ErrorAccessDenied }
        || ex is JobKeeperChildLaunchException { NativeErrorCode: ErrorAccessDenied };
}
