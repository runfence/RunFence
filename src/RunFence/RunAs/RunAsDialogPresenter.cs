using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.RunAs.UI;
using RunFence.RunAs.UI.Forms;

namespace RunFence.RunAs;

/// <summary>
/// Handles the secure desktop dialog presentation for the RunAs flow,
/// including lock/unlock and new account/container creation.
/// </summary>
public class RunAsDialogPresenter(
    IModalCoordinator modalCoordinator,
    RunAsDosProtection dosProtection,
    RunAsPermissionChecker permissionChecker,
    RunAsUserAccountCreator userAccountCreator,
    RunAsContainerCreator containerCreator,
    RunAsCredentialPersister credentialPersister,
    IAppStateProvider appState,
    IAppLockControl appLock,
    SessionContext session,
    IEvaluationLimitHelper evaluationLimitHelper,
    Func<RunAsDialog> dialogFactory)
{
    /// <summary>
    /// Handles lock/unlock, shows the RunAs dialog, and resolves new account/container requests.
    /// Returns the dialog result (possibly empty), or null if lock/unlock failed.
    /// Calls <paramref name="setUnlockedForRunAs"/> with true if the app was unlocked for this flow.
    /// </summary>
    public async Task<RunAsDialogResult?> ShowRunAsDialogAsync(string filePath, string? arguments,
        ShortcutContext? shortcutContext, bool isAdmin, Action<bool> setUnlockedForRunAs)
    {
        setUnlockedForRunAs(false);

        if (appLock.IsLocked)
        {
            if (!await appLock.TryUnlockAsync(isAdmin))
            {
                MessageBox.Show("Could not unlock the application.", "RunFence",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            setUnlockedForRunAs(true);
        }

        var empty = RunAsDialogResult.Empty();

        var credentials = session.CredentialStore.Credentials;
        var existingApps = appState.Database.Apps;

        var sidsNeedingPermission = permissionChecker.ComputeSidsNeedingPermission(
            filePath, credentials, appState.Database.AppContainers);

        RunAsDialogResult? capturedResult = null;
        DialogResult dlgResult = DialogResult.Cancel;
        bool createNewAccountRequested = false;
        bool createNewContainerRequested = false;

        modalCoordinator.RunOnSecureDesktop(() =>
        {
            var dlg = dialogFactory();
            dlg.Initialize(new RunAsDialogOptions(
                FilePath: filePath,
                Arguments: arguments,
                Credentials: credentials.ToList(),
                ExistingApps: existingApps.ToList(),
                LastUsedAccountSid: credentialPersister.LastUsedRunAsAccountSid,
                SidsNeedingPermission: sidsNeedingPermission,
                SidNames: appState.Database.SidNames,
                ShortcutContext: shortcutContext,
                AppContainers: appState.Database.AppContainers,
                LastUsedContainerName: credentialPersister.LastUsedRunAsContainerName,
                CurrentUserSid: SidResolutionHelper.GetCurrentUserSid(),
                AccountPrivilegeLevels: appState.Database.Accounts
                    .Where(a => !string.IsNullOrEmpty(a.Sid))
                    .ToDictionary(a => a.Sid, a => a.PrivilegeLevel,
                        StringComparer.OrdinalIgnoreCase)));
            using (dlg)
            {
                dlgResult = dlg.ShowDialog();
                if (dlgResult == DialogResult.OK)
                {
                    capturedResult = dlg.CaptureResult();
                    createNewAccountRequested = dlg.CreateNewAccountRequested;
                    createNewContainerRequested = dlg.CreateNewContainerRequested;
                }
            }
        });

        if (dlgResult != DialogResult.OK || capturedResult == null)
        {
            dosProtection.RecordDecline();
            return empty;
        }

        if (capturedResult.RevertShortcutRequested)
            return capturedResult;

        // Container path: return immediately without credential resolution
        if (capturedResult.SelectedContainer != null)
            return capturedResult;

        if (createNewContainerRequested)
        {
            capturedResult.AdHocPassword?.Dispose();
            var newContainer = containerCreator.CreateNewContainer();
            if (newContainer == null)
                return empty;
            return new RunAsDialogResult(
                Credential: null,
                SelectedContainer: newContainer,
                PermissionGrant: capturedResult.PermissionGrant,
                CreateAppEntryOnly: capturedResult.CreateAppEntryOnly,
                PrivilegeLevel: PrivilegeLevel.Basic,
                UpdateOriginalShortcut: capturedResult.UpdateOriginalShortcut,
                RevertShortcutRequested: false,
                EditExistingApp: capturedResult.EditExistingApp,
                ExistingAppForLaunch: capturedResult.ExistingAppForLaunch);
        }

        if (createNewAccountRequested)
        {
            capturedResult.AdHocPassword?.Dispose();

            if (!evaluationLimitHelper.CheckCredentialLimit(session.CredentialStore.Credentials))
            {
                dosProtection.RecordDecline();
                return empty;
            }

            var newCred = userAccountCreator.CreateNewAccount(filePath, dosProtection, out var newAccountGrant);
            if (newCred == null)
                return empty;
            var grantSelection = newAccountGrant ?? capturedResult.PermissionGrant;
            return new RunAsDialogResult(
                Credential: newCred,
                SelectedContainer: null,
                PermissionGrant: grantSelection,
                CreateAppEntryOnly: capturedResult.CreateAppEntryOnly,
                PrivilegeLevel: capturedResult.PrivilegeLevel,
                UpdateOriginalShortcut: capturedResult.UpdateOriginalShortcut,
                RevertShortcutRequested: false,
                EditExistingApp: capturedResult.EditExistingApp,
                ExistingAppForLaunch: capturedResult.ExistingAppForLaunch);
        }

        if (capturedResult.Credential == null)
        {
            capturedResult.AdHocPassword?.Dispose();
            return empty;
        }

        return capturedResult;
    }
}
