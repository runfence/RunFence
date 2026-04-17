using System.Security;
using RunFence.Account;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Container;

namespace RunFence.RunAs;

/// <summary>
/// Handles launching files without a saved app entry, either via stored credentials or an AppContainer token.
/// Extracted from RunAsAppEntryManager to keep it focused on app entry persistence.
/// </summary>
public class RunAsDirectLauncher(
    IAppStateProvider appState,
    ILaunchFacade facade,
    ISidNameCacheService sidNameCache,
    ISidResolver sidResolver,
    IRunAsLaunchErrorHandler launchErrorHandler)
{
    /// <summary>
    /// Launches a file without a saved app entry, using stored credentials directly.
    /// When <paramref name="adHocPassword"/> is provided, uses it directly without credential lookup.
    /// </summary>
    public void LaunchWithoutAppEntry(string filePath, string? arguments, CredentialEntry credential,
        bool isFolder = false, PrivilegeLevel privilegeLevel = PrivilegeLevel.Basic,
        SecureString? adHocPassword = null)
    {
        LaunchCredentials? launchCreds = null;
        if (adHocPassword != null)
        {
            var (domain, username) = SidNameResolver.ResolveDomainAndUsername(
                credential.Sid, isCurrentAccount: false, sidResolver, appState.Database.SidNames);
            launchCreds = new LaunchCredentials(adHocPassword, domain, username);
        }

        LaunchDirect(filePath, arguments, credential.Sid, isFolder, privilegeLevel, launchCreds);
    }

    /// <summary>
    /// Launches a file without a saved app entry, using an AppContainer token.
    /// </summary>
    public void LaunchWithoutAppEntryInContainer(string filePath, string? arguments,
        AppContainerEntry container, bool isFolder)
    {
        if (isFolder)
        {
            MessageBox.Show("Folder apps are not supported with AppContainers from the Run As flow.",
                "RunFence", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var permissionPrompt = AclPermissionDialogHelper.CreateLaunchPermissionPrompt(sidNameCache);
        var workingDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        RunWithLaunchErrorHandling(
            () => facade.LaunchFile(new ProcessLaunchTarget(filePath, arguments, workingDirectory),
                new AppContainerLaunchIdentity(container),
                permissionPrompt: permissionPrompt),
            filePath);
    }

    private void LaunchDirect(string filePath, string? arguments, string sid, bool isFolder,
        PrivilegeLevel privilegeLevel, LaunchCredentials? credentials)
    {
        var permissionPrompt = AclPermissionDialogHelper.CreateLaunchPermissionPrompt(sidNameCache);
        var identity = new AccountLaunchIdentity(sid)
        {
            PrivilegeLevel = privilegeLevel,
            Credentials = credentials,
        };

        if (isFolder)
        {
            RunWithLaunchErrorHandling(
                () => facade.LaunchFolderBrowser(identity, filePath, folderPermissionPrompt: permissionPrompt),
                filePath);
        }
        else
        {
            var workingDir = Path.GetDirectoryName(filePath);
            RunWithLaunchErrorHandling(
                () => facade.LaunchFile(new ProcessLaunchTarget(filePath, arguments, workingDir), identity,
                    permissionPrompt: permissionPrompt),
                filePath);
        }
    }

    private void RunWithLaunchErrorHandling(Action launchAction, string filePath)
        => launchErrorHandler.RunWithErrorHandling(launchAction, filePath);
}
