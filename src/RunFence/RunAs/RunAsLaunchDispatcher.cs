using RunFence.Acl.UI;
using RunFence.Account;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.RunAs.UI;

namespace RunFence.RunAs;

/// <summary>
/// Dispatches the RunAs dialog result to the appropriate action: opening the app edit dialog
/// for new entry creation, or launching the app directly (via existing entry or without one).
/// </summary>
public class RunAsLaunchDispatcher(
    RunAsAppEditDialogHandler dialogHandler,
    IAppEntryLauncher entryLauncher,
    RunAsDirectLauncher directLauncher,
    ISidNameCacheService sidNameCache,
    IAppStateProvider appState,
    IRunAsLaunchErrorHandler launchErrorHandler,
    RunAsAppShortcutCreator shortcutCreator)
{
    /// <summary>
    /// Dispatches the container flow result: opens the app edit dialog when
    /// <see cref="RunAsDialogResult.CreateAppEntryOnly"/> is set, otherwise launches.
    /// </summary>
    public void DispatchContainerResult(RunAsDialogResult result, string filePath, string? arguments,
        string? launcherWorkingDirectory, bool isFolder, string? originalLnkPath)
    {
        if (result.CreateAppEntryOnly)
        {
            dialogHandler.OpenAppEditDialogForContainer(result.EditExistingApp, filePath,
                result.SelectedContainer!, originalLnkPath, result.UpdateOriginalShortcut);
            return;
        }

        if (result.ExistingAppForLaunch != null)
            launchErrorHandler.RunWithErrorHandling(
                () => entryLauncher.Launch(result.ExistingAppForLaunch, arguments, launcherWorkingDirectory,
                    AclPermissionDialogHelper.CreateLaunchPermissionPrompt(sidNameCache)),
                filePath);
        else
            directLauncher.LaunchWithoutAppEntryInContainer(filePath, arguments, result.SelectedContainer!, isFolder);
    }

    /// <summary>
    /// Dispatches the credential flow result: opens the app edit dialog when
    /// <see cref="RunAsDialogResult.CreateAppEntryOnly"/> is set, otherwise launches.
    /// After launching, updates the original shortcut if requested.
    /// </summary>
    public void DispatchCredentialResult(RunAsDialogResult result, string filePath, string? arguments,
        string? launcherWorkingDirectory, bool isFolder, string? originalLnkPath)
    {
        if (result.CreateAppEntryOnly)
        {
            if (result.EditExistingApp != null)
                dialogHandler.OpenAppEditDialog(result.EditExistingApp, originalLnkPath: originalLnkPath,
                    updateOriginalShortcut: result.UpdateOriginalShortcut);
            else
            {
                var acct = appState.Database.GetAccount(result.Credential!.Sid);
                var accountDefault = acct?.PrivilegeLevel ?? PrivilegeLevel.Basic;
                PrivilegeLevel? privilegeLevel = result.PrivilegeLevel == accountDefault ? null : result.PrivilegeLevel;
                dialogHandler.OpenAppEditDialog(null, filePath, result.Credential,
                    originalLnkPath, result.UpdateOriginalShortcut,
                    privilegeLevel: privilegeLevel);
            }

            return;
        }

        if (result.ExistingAppForLaunch != null)
            launchErrorHandler.RunWithErrorHandling(
                () => entryLauncher.Launch(result.ExistingAppForLaunch, arguments, launcherWorkingDirectory,
                    AclPermissionDialogHelper.CreateLaunchPermissionPrompt(sidNameCache)),
                filePath);
        else
            directLauncher.LaunchWithoutAppEntry(filePath, arguments, result.Credential!, isFolder,
                result.PrivilegeLevel, result.AdHocPassword);

        if (result.UpdateOriginalShortcut && originalLnkPath != null)
        {
            var appId = result.ExistingAppForLaunch?.Id
                        ?? appState.Database.Apps.FirstOrDefault(a =>
                            string.Equals(a.AccountSid, result.Credential!.Sid, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(a.ExePath, filePath, StringComparison.OrdinalIgnoreCase))?.Id;

            if (appId != null)
                shortcutCreator.TryUpdateOriginalShortcut(originalLnkPath, appId);
        }
    }
}
