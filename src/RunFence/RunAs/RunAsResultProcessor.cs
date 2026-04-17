using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.RunAs.UI;

namespace RunFence.RunAs;

/// <summary>
/// Processes the result of the RunAs dialog: handles container and credential result flows,
/// including permission grants, app entry creation/launch, and shortcut revert.
/// </summary>
public class RunAsResultProcessor(
    RunAsPermissionApplier permissionApplier,
    RunAsLaunchDispatcher launchDispatcher,
    RunAsCredentialPersister credentialPersister,
    RunAsDosProtection dosProtection,
    IShortcutService shortcutService,
    ILoggingService log)
{
    /// <summary>
    /// Reverts the shortcut at <paramref name="originalLnkPath"/> to point back to the original
    /// app rather than the RunFence launcher. Logs the result.
    /// Throws on failure so the caller can present an appropriate error to the user.
    /// </summary>
    public void ProcessShortcutRevert(string originalLnkPath, AppEntry managedApp)
    {
        shortcutService.RevertSingleShortcut(originalLnkPath, managedApp);
        log.Info($"Reverted shortcut: {originalLnkPath}");
    }

    public void ProcessContainerResult(RunAsDialogResult result, string filePath, string? arguments,
        string? launcherWorkingDirectory, bool isFolder, string? originalLnkPath)
    {
        credentialPersister.SetLastUsedContainer(result.SelectedContainer!.Name);
        dosProtection.Reset();

        // Grant container SID access — same permission dialog flow as user accounts.
        // PathGrantService handles: ACE grant + AddGrant + traverse on ancestor directories
        // + container auto-grant for interactive user (AppContainer dual access check step 1).
        if (result.PermissionGrant != null)
            permissionApplier.ApplyContainerGrant(result.PermissionGrant, result.SelectedContainer.Sid);

        launchDispatcher.DispatchContainerResult(result, filePath, arguments, launcherWorkingDirectory, isFolder, originalLnkPath);
    }

    public void ProcessCredentialResult(RunAsDialogResult result, string filePath, string? arguments,
        string? launcherWorkingDirectory, bool isFolder, string? originalLnkPath)
    {
        credentialPersister.SetLastUsedAccount(result.Credential!.Sid);
        dosProtection.Reset();

        credentialPersister.TrySaveRememberedPassword(result);

        if (result.PermissionGrant != null)
            permissionApplier.ApplyCredentialGrant(result.PermissionGrant, result.Credential.Sid);

        launchDispatcher.DispatchCredentialResult(result, filePath, arguments, launcherWorkingDirectory, isFolder, originalLnkPath);
    }
}
