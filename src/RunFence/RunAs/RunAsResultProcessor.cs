using RunFence.Acl.Permissions;
using RunFence.Acl.QuickAccess;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Container;
using RunFence.Persistence;
using RunFence.RunAs.UI;

namespace RunFence.RunAs;

/// <summary>
/// Processes the result of the RunAs dialog: handles container and credential result flows,
/// including permission grants, app entry creation/launch, and shortcut revert.
/// </summary>
public class RunAsResultProcessor(
    RunAsAppEntryManager appEntryManager,
    RunAsAppEditDialogHandler dialogHandler,
    RunAsDirectLauncher directLauncher,
    IAppLaunchOrchestrator launchOrchestrator,
    IPermissionGrantService permissionGrantService,
    RunAsCredentialPersister credentialPersister,
    RunAsDosProtection dosProtection,
    IDatabaseService databaseService,
    IShortcutService shortcutService,
    SessionContext session,
    IAppStateProvider appState,
    ILoggingService log,
    IAppContainerService appContainerService,
    IQuickAccessPinService quickAccessPinService)
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
        // PermissionGrantService handles: ACE grant + AddGrant + traverse on ancestor directories
        // + container auto-grant for interactive user (AppContainer dual access check step 1).
        bool containerGrantsAdded = false;
        if (result.PermissionGrant != null)
        {
            try
            {
                var containerSid = appContainerService.GetSid(result.SelectedContainer.Name);
                containerGrantsAdded = permissionGrantService.EnsureAccess(
                    result.PermissionGrant.Path, containerSid,
                    result.PermissionGrant.Rights,
                    confirm: null).DatabaseModified;
            }
            catch (Exception ex)
            {
                log.Error("Failed to grant container permissions", ex);
            }
        }

        if (containerGrantsAdded)
        {
            using var scope = session.PinDerivedKey.Unprotect();
            databaseService.SaveConfig(appState.Database, scope.Data,
                session.CredentialStore.ArgonSalt);
        }

        if (result.CreateAppEntryOnly)
        {
            dialogHandler.OpenAppEditDialogForContainer(result.EditExistingApp, filePath, result.SelectedContainer,
                result.LaunchAsLowIntegrity, originalLnkPath, result.UpdateOriginalShortcut);
            return;
        }

        if (result.ExistingAppForLaunch != null)
            appEntryManager.RunWithLaunchErrorHandling(() => launchOrchestrator.Launch(result.ExistingAppForLaunch, arguments, launcherWorkingDirectory), filePath);
        else
            directLauncher.LaunchWithoutAppEntryInContainer(filePath, arguments, result.SelectedContainer, isFolder);
    }

    public void ProcessCredentialResult(RunAsDialogResult result, string filePath, string? arguments,
        string? launcherWorkingDirectory, bool isFolder, string? originalLnkPath)
    {
        credentialPersister.SetLastUsedAccount(result.Credential!.Sid);
        dosProtection.Reset();

        credentialPersister.TrySaveRememberedPassword(result);

        if (result.PermissionGrant != null)
        {
            try
            {
                var grantResult = permissionGrantService.EnsureAccess(
                    result.PermissionGrant.Path, result.Credential.Sid,
                    result.PermissionGrant.Rights,
                    confirm: null);
                if (grantResult.DatabaseModified)
                {
                    using var scope = session.PinDerivedKey.Unprotect();
                    databaseService.SaveConfig(appState.Database, scope.Data, session.CredentialStore.ArgonSalt);
                }
                if (grantResult.GrantAdded)
                    quickAccessPinService.PinFolders(result.Credential.Sid, [result.PermissionGrant.Path]);
            }
            catch (Exception ex)
            {
                log.Error("Failed to grant permissions", ex);
            }
        }

        if (result.CreateAppEntryOnly)
        {
            if (result.EditExistingApp != null)
                dialogHandler.OpenAppEditDialog(result.EditExistingApp, originalLnkPath: originalLnkPath,
                    updateOriginalShortcut: result.UpdateOriginalShortcut);
            else
            {
                var defaults = LaunchFlags.FromAccountDefaults(appState.Database, result.Credential.Sid);
                bool? launchAsLowIl = result.LaunchAsLowIntegrity == defaults.UseLowIntegrity ? null : result.LaunchAsLowIntegrity;
                bool? launchAsSplitToken = result.LaunchAsSplitToken == defaults.UseSplitToken ? null : result.LaunchAsSplitToken;
                dialogHandler.OpenAppEditDialog(null, filePath, result.Credential, launchAsLowIl,
                    originalLnkPath, result.UpdateOriginalShortcut,
                    launchAsSplitToken: launchAsSplitToken);
            }

            return;
        }

        if (result.ExistingAppForLaunch != null)
            appEntryManager.RunWithLaunchErrorHandling(() => launchOrchestrator.Launch(result.ExistingAppForLaunch, arguments, launcherWorkingDirectory), filePath);
        else
            directLauncher.LaunchWithoutAppEntry(filePath, arguments, result.Credential, isFolder, result.LaunchAsLowIntegrity, result.LaunchAsSplitToken, result.AdHocPassword);

        if (result.UpdateOriginalShortcut && originalLnkPath != null)
        {
            var appId = result.ExistingAppForLaunch?.Id
                        ?? appState.Database.Apps.FirstOrDefault(a =>
                            string.Equals(a.AccountSid, result.Credential!.Sid, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(a.ExePath, filePath, StringComparison.OrdinalIgnoreCase))?.Id;

            if (appId != null)
                appEntryManager.TryUpdateOriginalShortcut(originalLnkPath, appId);
        }
    }
}