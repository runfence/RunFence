using RunFence.Apps.Shortcuts;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.RunAs.UI;

namespace RunFence.RunAs;

/// <summary>
/// Processes the result of the RunAs dialog: handles container and credential result flows,
/// including permission grants, app entry creation/launch, and shortcut revert.
/// </summary>
public class RunAsResultProcessor(
    RunAsPermissionApplier permissionApplier,
    IRunAsLaunchDispatcher launchDispatcher,
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
            ApplyGrantWithUserMessage(() =>
                permissionApplier.ApplyContainerGrant(result.PermissionGrant, result.SelectedContainer.Sid));

        launchDispatcher.DispatchContainerResult(result, filePath, arguments, launcherWorkingDirectory, isFolder, originalLnkPath);
    }

    public void ProcessCredentialResult(RunAsDialogResult result, string filePath, string? arguments,
        string? launcherWorkingDirectory, bool isFolder, string? originalLnkPath)
    {
        credentialPersister.SetLastUsedAccount(result.Credential!.Sid);
        dosProtection.Reset();

        credentialPersister.TrySaveRememberedPassword(result);

        if (result.PermissionGrant != null)
            ApplyGrantWithUserMessage(() =>
                permissionApplier.ApplyCredentialGrant(result.PermissionGrant, result.Credential.Sid));

        launchDispatcher.DispatchCredentialResult(result, filePath, arguments, launcherWorkingDirectory, isFolder, originalLnkPath);
    }

    private static void ApplyGrantWithUserMessage(Action applyGrant)
    {
        try
        {
            applyGrant();
        }
        catch (GrantOperationException ex)
        {
            throw new InvalidOperationException(FormatGrantFailure(ex), ex);
        }
    }

    private static string FormatGrantFailure(GrantOperationException ex)
        => GrantApplyFailureFormatter.IsSaveFailureStep(ex.Step)
            ? $"RunFence could not save the permission grant before applying it: {ex.Cause.Message}"
            : $"RunFence saved the permission grant, but applying filesystem access failed: {ex.Cause.Message}";
}
