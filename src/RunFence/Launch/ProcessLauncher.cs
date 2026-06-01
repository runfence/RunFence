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
    IWindowsAppsActivationLauncher windowsAppsActivationLauncher,
    ILoggingService log)
    : IProcessLauncher, ILaunchIdentityAcceptor<ProcessInfo?>
{
    public ProcessInfo? Launch(LaunchIdentity identity, ProcessLaunchTarget target)
        => identity.Visit(this, target);

    public ProcessInfo? Accept(AccountLaunchIdentity identity, ProcessLaunchTarget? target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var creds = identity.Credentials ?? credentialsLookup.GetBySid(identity.Sid);
        try
        {
            var resolved = identity with { Credentials = creds };
            var packageIdentitySourcePath = ResolveWindowsAppsActivationSourcePath(target.ExePath, resolved.Sid);
            var launchTarget = ApplyWindowsAppsStartupFeedback(target, packageIdentitySourcePath);
            var r = LaunchAccountTarget(launchTarget, packageIdentitySourcePath, identity, resolved);
            log.Info($"Launched {target.ExePath} as {creds.Username ?? (creds.TokenSource == LaunchTokenSource.InteractiveUser ? "interactive user" : "current account")}");
            return r;
        }
        finally
        {
            if (identity.Credentials == null)
                creds.Password?.Dispose();
        }
    }

    public ProcessInfo? Accept(AppContainerLaunchIdentity identity, ProcessLaunchTarget? target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return appContainerLauncher.LaunchFile(target, identity);
    }

    private ProcessInfo? LaunchAccountTarget(
        ProcessLaunchTarget target,
        string? packageIdentitySourcePath,
        AccountLaunchIdentity originalIdentity,
        AccountLaunchIdentity resolvedIdentity)
    {
        if (packageIdentitySourcePath != null)
        {
            return windowsAppsActivationLauncher.TryLaunch(
                target,
                packageIdentitySourcePath,
                originalIdentity,
                resolvedIdentity);
        }

        var process = profileRepairHelper.ExecuteWithProfileRepair(
            () => accountProcessLauncher.Launch(target, resolvedIdentity),
            originalIdentity.Sid);
        return process
               ?? throw new InvalidOperationException(
                   $"Launch did not return a process for non-shell-wrapped target '{target.ExePath}'.");
    }

    private static ProcessLaunchTarget ApplyWindowsAppsStartupFeedback(
        ProcessLaunchTarget target,
        string? packageIdentitySourcePath)
    {
        if (target.SuppressStartupFeedback)
            return target;

        return packageIdentitySourcePath != null
            ? target with { SuppressStartupFeedback = true }
            : target;
    }

    private string? ResolveWindowsAppsActivationSourcePath(string exePath, string targetUserSid)
    {
        if (WindowsAppsPackagePathParser.TryParsePackagePath(exePath, out _))
            return exePath;

        if (windowsAppsAliasPathResolver.IsWindowsAppsAliasPath(exePath))
            return exePath;

        if (!Path.IsPathRooted(exePath))
        {
            var aliasPath = windowsAppsAliasPathResolver.TryResolveForUserSid(exePath, targetUserSid);
            if (aliasPath != null)
                return aliasPath;
        }

        try
        {
            var fullPath = Path.GetFullPath(exePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (fullPath.StartsWith(
                    windowsDirectory + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                && executableKindService.IsUwpExeFile(exePath))
            {
                return exePath;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }

        return null;
    }
}
