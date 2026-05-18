using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.RunAs.UI;
using RunFence.RunAs.UI.Forms;
using RunFence.Startup.UI;

namespace RunFence.RunAs;

/// <summary>
/// Handles the RunAs dialog presentation, including lock/unlock and new account/container creation.
/// Uses the secure desktop for IPC-originated requests; runs on the normal desktop for UI-originated requests.
/// Post-dialog routing is delegated to <see cref="RunAsPostDialogRouter"/>.
/// </summary>
public class RunAsDialogPresenter(
    IModalCoordinator modalCoordinator,
    RunAsPermissionChecker permissionChecker,
    RunAsCredentialPersister credentialPersister,
    IAppStateProvider appState,
    IAppLockControl appLock,
    IStartupUnlockGrant startupUnlockGrant,
    SessionContext session,
    RunAsPostDialogRouter postDialogRouter,
    Func<RunAsDialog> dialogFactory)
{
    /// <summary>
    /// Handles lock/unlock, shows the RunAs dialog, and resolves new account/container requests.
    /// Returns the dialog result (possibly empty), or null if lock/unlock failed.
    /// Calls <paramref name="setUnlockedForRunAs"/> with true if the app was unlocked for this flow.
    /// </summary>
    public async Task<RunAsDialogResult?> ShowRunAsDialogAsync(string filePath, string? arguments,
        ShortcutContext? shortcutContext, string? initialAccountSid, bool isAdmin,
        Action<bool> setUnlockedForRunAs, bool useSecureDesktop)
    {
        setUnlockedForRunAs(false);

        if (appLock.IsLocked)
        {
            var startupUnlockApprovedByAdmin = startupUnlockGrant.TryConsume();
            var unlockApprovedByAdmin = isAdmin || startupUnlockApprovedByAdmin;
            var unlockResult = await appLock.TryUnlockForOperationWithResultAsync(unlockApprovedByAdmin);
            if (unlockResult is OperationUnlockResult.Declined or OperationUnlockResult.Failed)
                postDialogRouter.RecordUnlockDecline();
            if (unlockResult != OperationUnlockResult.Succeeded)
            {
                return null;
            }

            setUnlockedForRunAs(true);
        }

        var credentials = session.CredentialStore.Credentials;
        var existingApps = appState.Database.Apps;

        var sidsNeedingPermission = permissionChecker.ComputeSidsNeedingPermission(
            filePath, credentials, appState.Database.AppContainers);

        RunAsDialogResult? capturedResult = null;
        DialogResult dlgResult = DialogResult.Cancel;
        bool createNewAccountRequested = false;
        bool createNewContainerRequested = false;

        Action<Action> runModal = useSecureDesktop
            ? modalCoordinator.RunOnSecureDesktop
            : modalCoordinator.RunModal;

        runModal(() =>
        {
            var dlg = dialogFactory();
            dlg.Initialize(new RunAsDialogOptions(
                FilePath: filePath,
                Arguments: arguments,
                Credentials: credentials.ToList(),
                ExistingApps: existingApps.ToList(),
                InitialAccountSid: initialAccountSid,
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
                        StringComparer.OrdinalIgnoreCase),
                ShowSystemInRunAs: appState.Database.ShowSystemInRunAs));
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

        return await postDialogRouter.RouteAsync(
            dlgResult,
            capturedResult,
            createNewAccountRequested,
            createNewContainerRequested,
            filePath,
            credentials);
    }
}
