using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Account.UI;

public interface IWindowsTerminalPrivilegeLaunchExecutableService
{
    string PrepareLaunchExecutablePath(PrivilegeLevel privilegeLevel);
}

public sealed class WindowsTerminalPrivilegeLaunchExecutableService(
    WindowsTerminalDeploymentPaths deploymentPaths,
    IProgramDataPathPolicyService programDataPathPolicyService,
    IClock clock,
    ILoggingService log)
    : IWindowsTerminalPrivilegeLaunchExecutableService
{
    private static readonly TimeSpan LaunchExecutableRetention = TimeSpan.FromDays(1);
    private readonly object launchExecutableGate = new();
    private readonly Dictionary<string, DateTime> launchExecutableCreatedUtc = new(StringComparer.OrdinalIgnoreCase);

    public string PrepareLaunchExecutablePath(PrivilegeLevel privilegeLevel)
    {
        if (privilegeLevel is not (PrivilegeLevel.Isolated or PrivilegeLevel.LowIntegrity))
            throw new ArgumentOutOfRangeException(nameof(privilegeLevel), privilegeLevel, null);

        var sourceExecutablePath = deploymentPaths.GetSharedExecutablePath(privilegeLevel);
        if (!File.Exists(sourceExecutablePath))
            return deploymentPaths.SharedExecutablePath;

        lock (launchExecutableGate)
        {
            TryDeleteStaleLaunchExecutables(privilegeLevel);

            var launchExecutablePath = deploymentPaths.CreatePrivilegeLaunchExecutablePath(privilegeLevel);
            if (!programDataPathPolicyService.IsUnderRoot(sourceExecutablePath) ||
                !programDataPathPolicyService.IsUnderRoot(launchExecutablePath))
            {
                throw new InvalidOperationException("Windows Terminal launch executable path is outside ProgramData.");
            }

            try
            {
                WindowsTerminalHardLinkNative.CreateFileHardLink(launchExecutablePath, sourceExecutablePath);
                launchExecutableCreatedUtc[launchExecutablePath] = clock.UtcNow;
                return launchExecutablePath;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryDeleteFile(launchExecutablePath);
                log.Warn($"Windows Terminal {GetLogName(privilegeLevel)} launch hard link could not be created and launch will use the shared executable: {ex.Message}");
                return sourceExecutablePath;
            }
        }
    }

    private void TryDeleteStaleLaunchExecutables(PrivilegeLevel privilegeLevel)
    {
        var cutoffUtc = clock.UtcNow - LaunchExecutableRetention;
        foreach (var launchExecutablePath in deploymentPaths.GetPrivilegeLaunchExecutablePaths(privilegeLevel))
        {
            if (launchExecutableCreatedUtc.TryGetValue(launchExecutablePath, out var createdUtc) && createdUtc >= cutoffUtc)
                continue;

            TryDeleteFile(launchExecutablePath);
            launchExecutableCreatedUtc.Remove(launchExecutablePath);
        }
    }

    private static string GetLogName(PrivilegeLevel privilegeLevel)
        => privilegeLevel switch
        {
            PrivilegeLevel.Isolated => "isolated",
            PrivilegeLevel.LowIntegrity => "low-integrity",
            _ => throw new ArgumentOutOfRangeException(nameof(privilegeLevel), privilegeLevel, null)
        };

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
