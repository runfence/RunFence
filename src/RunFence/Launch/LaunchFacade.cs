using System.Security.AccessControl;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;
using RunFence.Launch.Tokens;
using RunFence.Persistence;

namespace RunFence.Launch;

public class LaunchFacade(
    IProcessLauncher processLauncher,
    ILaunchDefaultsResolver defaultsResolver,
    IPathGrantService pathGrantService,
    IDatabaseService databaseService,
    ISessionProvider sessionProvider,
    ISidResolver sidResolver,
    IUiThreadInvoker uiThreadInvoker)
    : ILaunchFacade
{
    public ProcessInfo? LaunchFile(ProcessLaunchTarget target, LaunchIdentity identity, Func<string, string, bool>? permissionPrompt = null)
    {
        var resolved = defaultsResolver.ResolveDefaults(identity);
        var (wrappedTarget, isWrapped) = ProcessLaunchHelper.WrapTargetForLaunch(target);
        bool unelevated = resolved.IsUnelevated ?? true;

        string? grantPermissionToExePath;
        if (PathHelper.IsOwnDir(target.ExePath))
            grantPermissionToExePath = target.ExePath;
        else
        {
            if (permissionPrompt != null)
            {
                var ensureAccessResult = pathGrantService.EnsureAccess(
                    resolved.Sid, Path.GetDirectoryName(target.ExePath) ?? string.Empty,
                    FileSystemRights.ReadAndExecute, permissionPrompt, unelevated);
                if (ensureAccessResult.DatabaseModified)
                    SaveConfigAsync();
            }

            grantPermissionToExePath = null;
        }

        if (permissionPrompt != null
            && !string.IsNullOrEmpty(target.WorkingDirectory)
            && !string.Equals(
                target.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar),
                (Path.GetDirectoryName(target.ExePath) ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            if (pathGrantService.EnsureAccess(
                    resolved.Sid, target.WorkingDirectory, FileSystemRights.ReadAndExecute,
                    permissionPrompt, unelevated).DatabaseModified)
                SaveConfigAsync();
        }

        var result = LaunchCore(wrappedTarget, resolved, grantPermissionToExePath);
        if (isWrapped)
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
            else
                folderPath = sidResolver.TryGetStartMenuProgramsPath(resolved.Sid,
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
            if (pathGrantService.EnsureAccess(
                    resolved.Sid, folderPath, FileSystemRights.ReadAndExecute,
                    folderPermissionPrompt, resolved.IsUnelevated ?? true).DatabaseModified)
                SaveConfigAsync();
        }

        var target = new ProcessLaunchTarget(folderBrowserExe, resolvedArgs, folderPath);
        var (wrappedTarget, isWrapped) = ProcessLaunchHelper.WrapTargetForLaunch(target);
        var result = LaunchCore(wrappedTarget, resolved, folderBrowserExe);
        if (isWrapped) { result?.Dispose(); return null; }
        return result;
    }

    public void LaunchUrl(string url, LaunchIdentity identity)
    {
        if (url.Length > 32000)
            throw new ArgumentException($"URL is too long ({url.Length} characters). Maximum allowed is 32000.", nameof(url));
        var resolved = defaultsResolver.ResolveDefaults(identity);
        var target = ProcessLaunchHelper.BuildUrlLaunchTarget(url);
        LaunchCore(target, resolved, null)?.Dispose();
    }

    private ProcessInfo? LaunchCore(ProcessLaunchTarget target, LaunchIdentity identity, string? grantPermissionToExePath)
    {
        if (grantPermissionToExePath != null)
        {
            var exeDir = Path.GetDirectoryName(grantPermissionToExePath);
            if (!string.IsNullOrEmpty(exeDir)
                && pathGrantService.EnsureAccess(identity.Sid, exeDir,
                    FileSystemRights.ReadAndExecute, confirm: null,
                    unelevated: identity.IsUnelevated ?? true).DatabaseModified)
            {
                SaveConfigAsync();
            }
        }

        return processLauncher.Launch(identity, target);
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
