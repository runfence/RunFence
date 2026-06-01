using System.Runtime.ExceptionServices;
using Microsoft.Win32;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence;

namespace RunFence.Account.UI;

public interface IWindowsTerminalAccountStateService
{
    bool IsInstalledForAccount(string sid);
    string ResolveLaunchTarget(string sid);
    string ResolveLaunchTarget(AccountLaunchIdentity identity);
}

public sealed class WindowsTerminalAccountStateService(
    AccountToolResolver toolResolver,
    IDatabaseProvider databaseProvider,
    IUserHiveManager userHiveManager,
    IHkuRootProvider hkuRootProvider,
    WindowsTerminalDeploymentPaths deploymentPaths,
    IWindowsTerminalPrivilegeLaunchExecutableService privilegeLaunchExecutableService)
    : IWindowsTerminalAccountStateService
{
    public bool IsInstalledForAccount(string sid)
        => ResolveNativeWindowsTerminal(sid) != null || UserPathContainsSharedPath(sid);

    public string ResolveLaunchTarget(string sid)
        => File.Exists(deploymentPaths.SharedExecutablePath)
            ? deploymentPaths.SharedExecutablePath
            : ResolveNativeWindowsTerminal(sid) ?? "cmd.exe";

    public string ResolveLaunchTarget(AccountLaunchIdentity identity)
    {
        if (File.Exists(deploymentPaths.SharedExecutablePath))
        {
            var privilegeLevel = ResolveEffectivePrivilegeLevel(identity);
            if (privilegeLevel is PrivilegeLevel.Isolated or PrivilegeLevel.LowIntegrity)
                return privilegeLaunchExecutableService.PrepareLaunchExecutablePath(privilegeLevel);

            var sharedExecutablePath = deploymentPaths.GetSharedExecutablePath(privilegeLevel);
            return File.Exists(sharedExecutablePath)
                ? sharedExecutablePath
                : deploymentPaths.SharedExecutablePath;
        }

        return ResolveNativeWindowsTerminal(identity.Sid) ?? "cmd.exe";
    }

    private string? ResolveNativeWindowsTerminal(string sid)
        => toolResolver.ResolveWindowsAppsExe(sid, "wt.exe");

    private PrivilegeLevel ResolveEffectivePrivilegeLevel(AccountLaunchIdentity identity)
        => identity.PrivilegeLevel
           ?? databaseProvider.GetDatabase().GetAccount(identity.Sid)?.PrivilegeLevel
           ?? PrivilegeLevel.Isolated;

    private bool UserPathContainsSharedPath(string sid)
    {
        if (userHiveManager.IsHiveLoaded(sid))
            return ReadLoadedUserPathContainsSharedPath(sid);

        return RunOnBackgroundThread(() =>
        {
            using var lease = userHiveManager.EnsureHiveLoaded(sid);
            if (lease == null && !userHiveManager.IsHiveLoaded(sid))
                return false;

            return ReadLoadedUserPathContainsSharedPath(sid);
        });
    }

    private bool ReadLoadedUserPathContainsSharedPath(string sid)
    {
        using var usersRoot = hkuRootProvider.OpenUsersRoot();
        using var environmentKey = usersRoot.OpenSubKey($@"{sid}\Environment");
        var pathValue = environmentKey?.GetValue("Path") as string;
        if (string.IsNullOrWhiteSpace(pathValue))
            return false;

        var expectedPath = NormalizePath(deploymentPaths.SharedHelperPathDirectory);
        foreach (var entry in pathValue.Split(';'))
        {
            var normalizedEntry = NormalizePath(entry);
            if (normalizedEntry != null &&
                string.Equals(normalizedEntry, expectedPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static T RunOnBackgroundThread<T>(Func<T> action)
    {
        T? result = default;
        ExceptionDispatchInfo? capturedException = null;
        using var completed = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                capturedException = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true
        };
        thread.Start();
        completed.Wait();
        capturedException?.Throw();
        return result!;
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var trimmed = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (trimmed.Length == 0)
            return null;

        try
        {
            if (Path.IsPathFullyQualified(trimmed))
            {
                return Path.GetFullPath(trimmed)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
        catch
        {
            return null;
        }

        return trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
