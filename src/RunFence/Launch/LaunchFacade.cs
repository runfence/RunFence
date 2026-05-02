using System.Security.AccessControl;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch.Container;
using RunFence.Launch.Tokens;
using RunFence.Persistence;

namespace RunFence.Launch;

public class LaunchFacade(
    IProcessLauncher processLauncher,
    ILaunchDefaultsResolver defaultsResolver,
    ILaunchTargetResolver launchTargetResolver,
    ILaunchAccessManager launchAccessManager,
    IDatabaseService databaseService,
    ISessionProvider sessionProvider,
    IProfilePathResolver profilePathResolver,
    IUiThreadInvoker uiThreadInvoker)
    : ILaunchFacade
{
    public ProcessInfo? LaunchFile(ProcessLaunchTarget target, LaunchIdentity identity, Func<string, string, bool>? permissionPrompt = null)
    {
        var resolved = defaultsResolver.ResolveDefaults(identity);
        var traversal = launchTargetResolver.TraversePath(target.ExePath, resolved);
        bool unelevated = resolved.IsUnelevated ?? true;

        if (traversal.IsFolder)
            return LaunchFolderBrowser(identity, traversal.TraversedPath, permissionPrompt);

        var traversedTarget = target with
        {
            ExePath = traversal.TraversedPath,
            Arguments = string.IsNullOrEmpty(target.Arguments) ? traversal.ShortcutArguments : target.Arguments,
            WorkingDirectory = string.IsNullOrEmpty(target.WorkingDirectory) ? traversal.ShortcutWorkingDirectory : target.WorkingDirectory
        };

        string? grantPermissionToExePath;
        if (PathHelper.IsOwnDir(traversal.TraversedPath))
            grantPermissionToExePath = traversal.TraversedPath;
        else
        {
            if (permissionPrompt != null)
            {
                var ensureAccessResult = launchAccessManager.EnsureAccess(resolved,
                    Path.GetDirectoryName(traversal.TraversedPath) ?? string.Empty,
                    FileSystemRights.ReadAndExecute, permissionPrompt, unelevated);
                if (ensureAccessResult.DatabaseModified)
                    SaveConfigAsync();
            }

            grantPermissionToExePath = null;
        }

        if (permissionPrompt != null
            && !string.IsNullOrEmpty(traversedTarget.WorkingDirectory)
            && !string.Equals(
                traversedTarget.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar),
                (Path.GetDirectoryName(traversal.TraversedPath) ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            if (launchAccessManager.EnsureAccess(resolved, traversedTarget.WorkingDirectory,
                    FileSystemRights.ReadAndExecute, permissionPrompt, unelevated).DatabaseModified)
                SaveConfigAsync();
        }

        using var resolution = launchTargetResolver.ResolveFileHandler(resolved, traversedTarget);
        var result = LaunchCore(resolution.Target, resolved, grantPermissionToExePath);
        if (resolution.Kind == LaunchResolutionKind.ShellWrapped)
        {
            result?.Dispose();
            return null;
        }

        return result;
    }

    public ProcessInfo? LaunchFolderBrowser(LaunchIdentity identity, string? folderPath = null,
        Func<string, string, bool>? folderPermissionPrompt = null)
    {
        var resolved = defaultsResolver.ResolveDefaults(identity);

        if (string.IsNullOrEmpty(folderPath))
        {
            if (resolved is AppContainerLaunchIdentity c)
                folderPath = AppContainerPaths.GetContainerDataPath(c.Entry.Name);
            else if (SidResolutionHelper.IsSystemSid(resolved.Sid))
                folderPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
            else
                folderPath = profilePathResolver.TryGetStartMenuProgramsPath(resolved.Sid,
                                 string.Equals(SidResolutionHelper.GetCurrentUserSid(), resolved.Sid,
                                     StringComparison.OrdinalIgnoreCase))
                             ?? throw new InvalidOperationException(
                                 $"Profile path not found in registry for SID {resolved.Sid}.");
        }

        folderPath = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var session = sessionProvider.GetSession();
        var folderBrowserExe = PathHelper.ResolveExePath(session.Database.Settings.FolderBrowserExePath);
        var resolvedArgs = session.Database.Settings.FolderBrowserArguments.Replace("%1", folderPath);

        if (resolved is not AppContainerLaunchIdentity && folderPermissionPrompt != null)
        {
            if (launchAccessManager.EnsureAccess(resolved, folderPath,
                    FileSystemRights.ReadAndExecute, folderPermissionPrompt,
                    resolved.IsUnelevated ?? true).DatabaseModified)
                SaveConfigAsync();
        }

        var target = new ProcessLaunchTarget(folderBrowserExe, resolvedArgs, folderPath);
        using var resolution = launchTargetResolver.ResolveFileHandler(resolved, target);
        var result = LaunchCore(resolution.Target, resolved, folderBrowserExe);
        if (resolution.Kind == LaunchResolutionKind.ShellWrapped) { result?.Dispose(); return null; }
        return result;
    }

    public void LaunchUrl(string url, LaunchIdentity identity)
    {
        if (url.Length > 32000)
            throw new ArgumentException($"URL is too long ({url.Length} characters). Maximum allowed is 32000.", nameof(url));
        var resolved = defaultsResolver.ResolveDefaults(identity);
        using var resolution = launchTargetResolver.ResolveUrlHandler(resolved, url);
        LaunchCore(resolution.Target, resolved, null)?.Dispose();
    }

    private ProcessInfo? LaunchCore(ProcessLaunchTarget target, LaunchIdentity identity, string? grantPermissionToExePath)
    {
        EnsureReadExecuteAccess(identity, AppContext.BaseDirectory);

        if (grantPermissionToExePath != null)
        {
            var exeDir = Path.GetDirectoryName(grantPermissionToExePath);
            if (!string.IsNullOrEmpty(exeDir) && !PathHelper.IsSamePath(exeDir, AppContext.BaseDirectory))
                EnsureReadExecuteAccess(identity, exeDir);
        }

        return processLauncher.Launch(identity, target);
    }

    private void EnsureReadExecuteAccess(LaunchIdentity identity, string path)
    {
        if (launchAccessManager.EnsureAccess(identity, path,
                FileSystemRights.ReadAndExecute, confirm: null,
                unelevated: identity.IsUnelevated ?? true).DatabaseModified)
        {
            SaveConfigAsync();
        }
    }

    private void SaveConfigAsync()
    {
        uiThreadInvoker.BeginInvoke(() =>
        {
            var s = sessionProvider.GetSession();
            using var scope = s.PinDerivedKey.Unprotect();
            databaseService.SaveConfig(s.Database, scope.Data, s.CredentialStore.ArgonSalt);
        });
    }
}
