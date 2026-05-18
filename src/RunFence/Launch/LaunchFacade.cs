using System.Security.AccessControl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;
using RunFence.Apps;

namespace RunFence.Launch;

public class LaunchFacade(
    IProcessLauncher processLauncher,
    ILaunchDefaultsResolver defaultsResolver,
    ILaunchTargetResolver launchTargetResolver,
    ILaunchAccessManager launchAccessManager,
    ILoggingService log,
    UiThreadDatabaseAccessor dbAccessor,
    IProfilePathResolver profilePathResolver,
    IFolderHandlerService folderHandlerService,
    IAssociationAutoSetService associationAutoSetService,
    IAppContainerPathProvider appContainerPathProvider)
    : ILaunchFacade
{
    public LaunchExecutionResult LaunchFile(ProcessLaunchTarget target, LaunchIdentity identity, Func<string, string, bool>? permissionPrompt = null)
    {
        var databaseSnapshot = CaptureDatabaseSnapshot();
        var resolved = defaultsResolver.ResolveDefaults(identity, databaseSnapshot);
        var traversal = launchTargetResolver.TraversePath(target.ExePath, resolved, databaseSnapshot);

        if (traversal.IsFolder)
            return LaunchFolderBrowserCore(
                resolved,
                databaseSnapshot,
                traversal.TraversedPath,
                permissionPrompt,
                isTargetApproved: false);

        var traversedTarget = target with
        {
            ExePath = traversal.TraversedPath,
            Arguments = string.IsNullOrEmpty(target.Arguments) ? traversal.ShortcutArguments : target.Arguments,
            WorkingDirectory = string.IsNullOrEmpty(target.WorkingDirectory) ? traversal.ShortcutWorkingDirectory : target.WorkingDirectory,
            IsPathApproved = PathHelper.IsSamePath(target.ExePath, traversal.TraversedPath) && target.IsPathApproved
        };

        using var resolution = launchTargetResolver.ResolveFileHandler(
            resolved,
            traversedTarget,
            databaseSnapshot,
            traversal.Extension);
        return LaunchCore(
            resolution,
            resolved,
            permissionPrompt,
            new GrantPath(target.ExePath, false, target.IsPathApproved));
    }

    public LaunchExecutionResult LaunchFolderBrowser(LaunchIdentity identity, string? folderPath = null,
        Func<string, string, bool>? folderPermissionPrompt = null, bool isTargetApproved = true)
    {
        var databaseSnapshot = CaptureDatabaseSnapshot();
        var resolved = defaultsResolver.ResolveDefaults(identity, databaseSnapshot);
        return LaunchFolderBrowserCore(
            resolved,
            databaseSnapshot,
            folderPath,
            folderPermissionPrompt,
            isTargetApproved);
    }

    private LaunchExecutionResult LaunchFolderBrowserCore(
        LaunchIdentity resolved,
        AppDatabase databaseSnapshot,
        string? folderPath,
        Func<string, string, bool>? folderPermissionPrompt,
        bool isTargetApproved)
    {
        if (string.IsNullOrEmpty(folderPath))
        {
            if (resolved is AppContainerLaunchIdentity c)
                folderPath = appContainerPathProvider.GetContainerDataPath(c.Entry.Name);
            else if (SidResolutionHelper.IsSystemSid(resolved.Sid))
                folderPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
            else
                folderPath = profilePathResolver.TryGetStartMenuProgramsPath(resolved.Sid,
                                 string.Equals(SidResolutionHelper.GetCurrentUserSid(), resolved.Sid,
                                     StringComparison.OrdinalIgnoreCase))
                             ?? throw new InvalidOperationException(
                                 $"Profile path not found in registry for SID {resolved.Sid}.");
        }

        var normalizedFolderPath = Path.GetFullPath(folderPath);
        var root = Path.GetPathRoot(normalizedFolderPath);
        folderPath = !string.IsNullOrEmpty(root) && PathHelper.IsSamePath(normalizedFolderPath, root)
            ? root
            : normalizedFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var folderBrowserExe = PathHelper.ResolveExePath(databaseSnapshot.Settings.FolderBrowserExePath);
        var folderBrowserArguments = databaseSnapshot.Settings.FolderBrowserArguments ?? string.Empty;
        var resolvedArgs = folderBrowserArguments.Contains("%1", StringComparison.Ordinal)
            ? ProcessLaunchHelper.ApplySingleArgumentTemplate(folderBrowserArguments, folderPath)
            : folderBrowserArguments;

        var target = new ProcessLaunchTarget(folderBrowserExe, resolvedArgs, folderPath);
        using var resolution = launchTargetResolver.ResolveFileHandler(resolved, target, databaseSnapshot);
        return LaunchCore(
            resolution,
            resolved,
            folderPermissionPrompt,
            new GrantPath(folderBrowserExe, false, true),
            new GrantPath(folderPath, true, isTargetApproved));
    }

    public LaunchExecutionResult LaunchUrl(string url, LaunchIdentity identity)
    {
        if (url.Length > 32000)
            throw new ArgumentException($"URL is too long ({url.Length} characters). Maximum allowed is 32000.", nameof(url));
        var databaseSnapshot = CaptureDatabaseSnapshot();
        var resolved = defaultsResolver.ResolveDefaults(identity, databaseSnapshot);
        using var resolution = launchTargetResolver.ResolveUrlHandler(resolved, url, databaseSnapshot);
        return LaunchCore(resolution, resolved, null);
    }

    record struct GrantPath(string Path, bool IsDirectory, bool IsSilent);

    private AppDatabase CaptureDatabaseSnapshot()
        => dbAccessor.CreateSnapshot();

    private LaunchExecutionResult LaunchCore(
        LaunchTargetResolutionResult resolution,
        LaunchIdentity identity,
        Func<string, string, bool>? permissionPrompt,
        params GrantPath[] extraGrantPaths)
    {
        IEnumerable<GrantPath> GetGrantPaths()
        {
            yield return new GrantPath(AppContext.BaseDirectory, true, true);

            string? dir;
            foreach (var gp in extraGrantPaths)
            {
                dir = gp.IsDirectory ? gp.Path : Path.GetDirectoryName(gp.Path);

                if (!string.IsNullOrEmpty(dir))
                    yield return new GrantPath(dir, true, gp.IsSilent);
            }

            if (resolution.Kind is not LaunchResolutionKind.Script and not LaunchResolutionKind.ShellWrapped)
            {
                dir = Path.GetDirectoryName(resolution.Target.ExePath);
                if (!string.IsNullOrEmpty(dir))
                    yield return new GrantPath(dir, true, false);
            }
            
            dir = resolution.Target.WorkingDirectory;
            if (!string.IsNullOrEmpty(dir))
                yield return new GrantPath(dir, true, false);
        }

        var grantPaths = GetGrantPaths()
            .Select(x => x with { Path = PathHelper.NormalizeComparablePath(x.Path) })
            .OrderBy(x => x.IsSilent ? 0 : 1)
            .DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
        
        foreach (var gp in grantPaths)
        {
            if (PathHelper.IsOwnDir(gp.Path) || gp.IsSilent)
            {
                launchAccessManager.EnsureAccess(
                    identity,
                    gp.Path,
                    FileSystemRights.ReadAndExecute,
                    null,
                    identity.IsUnelevated ?? true);
            }
            else if (permissionPrompt is not null)
            {
                launchAccessManager.EnsureAccess(
                    identity,
                    gp.Path,
                    FileSystemRights.ReadAndExecute,
                    permissionPrompt,
                    identity.IsUnelevated ?? true);
            }
        }

        var process = processLauncher.Launch(identity, resolution.Target);

        if (resolution.Kind != LaunchResolutionKind.ShellWrapped && process == null)
        {
            throw new InvalidOperationException(
                $"Launch did not return a process for non-shell-wrapped target '{resolution.Target.ExePath}'.");
        }

        var maintenanceWarnings = new List<string>();
        log.Info(
            $"Post-launch maintenance started for '{resolution.Target.ExePath}' " +
            $"(kind={resolution.Kind}, sid={identity.Sid}, hasProcess={process != null})");

        if (identity is AccountLaunchIdentity accountIdentity)
        {
            log.Info($"Post-launch step started: folder-browser registration refresh for '{accountIdentity.Sid}'");
            try
            {
                var registrationResult = folderHandlerService.Register(accountIdentity.Sid);
                if (registrationResult?.Warnings is { Count: > 0 } warnings)
                {
                    foreach (var warning in warnings)
                    {
                        maintenanceWarnings.Add(warning);
                    }
                }
            }
            catch (Exception ex)
            {
                maintenanceWarnings.Add($"RunFence could not refresh folder-browser registration for '{accountIdentity.Sid}': {ex.Message}");
            }

            log.Info($"Post-launch step started: user association refresh for '{accountIdentity.Sid}'");
            try
            {
                associationAutoSetService.AutoSetForUser(accountIdentity.Sid);
            }
            catch (Exception ex)
            {
                maintenanceWarnings.Add($"RunFence could not refresh user associations for '{accountIdentity.Sid}': {ex.Message}");
            }
        }

        if (resolution.Kind == LaunchResolutionKind.ShellWrapped)
        {
            log.Info("Post-launch step started: shell-wrapper launch handle release");
            try
            {
                process?.Dispose();
            }
            catch (Exception ex)
            {
                maintenanceWarnings.Add($"RunFence could not release the shell-wrapper launch handle: {ex.Message}");
            }

            log.Info(
                $"Post-launch maintenance finished for '{resolution.Target.ExePath}' " +
                $"(warnings={maintenanceWarnings.Count})");
            return new LaunchExecutionResult(LaunchExecutionStatus.ShellWrappedNoProcess, null, maintenanceWarnings);
        }

        log.Info(
            $"Post-launch maintenance finished for '{resolution.Target.ExePath}' " +
            $"(warnings={maintenanceWarnings.Count})");

        return new LaunchExecutionResult(
            maintenanceWarnings.Count == 0
                ? LaunchExecutionStatus.ProcessStarted
                : LaunchExecutionStatus.ProcessStartedWithMaintenanceWarnings,
            process,
            maintenanceWarnings);
    }
}
