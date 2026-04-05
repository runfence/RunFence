using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.RunAs.UI;
using RunFence.RunAs.UI.Forms;
using RunFence.Security;
using RunFence.UI.Forms;

namespace RunFence.RunAs;

/// <summary>
/// Handles the secure desktop dialog presentation for the RunAs flow,
/// including lock/unlock and new account/container creation.
/// </summary>
public class RunAsDialogPresenter(
    ISecureDesktopRunner secureDesktop,
    RunAsDosProtection dosProtection,
    RunAsPermissionChecker permissionChecker,
    RunAsAccountCreator accountCreator,
    RunAsCredentialPersister credentialPersister,
    IAppStateProvider appState,
    IAppLockControl appLock,
    SessionContext session,
    Func<RunAsDialog> dialogFactory)
{
    /// <summary>
    /// Handles lock/unlock, shows the RunAs dialog, and resolves new account/container requests.
    /// Returns the dialog result (possibly empty), or null if lock/unlock failed.
    /// Sets <paramref name="unlockedForRunAs"/> to true if the app was unlocked for this flow.
    /// </summary>
    public RunAsDialogResult? ShowRunAsDialog(string filePath, string? arguments,
        ShortcutContext? shortcutContext, bool isAdmin, out bool unlockedForRunAs)
    {
        unlockedForRunAs = false;

        if (appLock.IsLocked)
        {
            if (!appLock.TryUnlock(isAdmin))
            {
                MessageBox.Show("Could not unlock the application.", "RunFence",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            unlockedForRunAs = true;
        }

        var empty = new RunAsDialogResult(null, null, null, false, false, false, false, false, null, null);

        var credentials = session.CredentialStore.Credentials;
        var existingApps = appState.Database.Apps;

        var sidsNeedingPermission = permissionChecker.ComputeSidsNeedingPermission(
            filePath, credentials, appState.Database.AppContainers);

        RunAsDialogResult? capturedResult = null;
        DialogResult dlgResult = DialogResult.Cancel;
        bool createNewAccountRequested = false;
        bool createNewContainerRequested = false;

        DataPanel.BeginModal();
        try
        {
            secureDesktop.Run(() =>
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
                    SplitTokenOptOutSids: appState.Database.Accounts.Where(a => a.SplitTokenOptOut).Select(a => a.Sid).ToList(),
                    LowIntegrityDefaultSids: appState.Database.Accounts.Where(a => a.LowIntegrityDefault).Select(a => a.Sid).ToList()));
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
        }
        finally
        {
            DataPanel.EndModal();
        }

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
            var newContainer = accountCreator.CreateNewContainer();
            if (newContainer == null)
                return empty;
            return new RunAsDialogResult(null, newContainer, capturedResult.PermissionGrant,
                capturedResult.CreateAppEntryOnly, capturedResult.LaunchAsLowIntegrity, false,
                capturedResult.UpdateOriginalShortcut, false, capturedResult.EditExistingApp, capturedResult.ExistingAppForLaunch);
        }

        if (createNewAccountRequested)
        {
            capturedResult.AdHocPassword?.Dispose();
            var newCred = accountCreator.CreateNewAccount(filePath, dosProtection, out var newAccountGrant);
            if (newCred == null)
                return empty;
            var grantSelection = newAccountGrant ?? capturedResult.PermissionGrant;
            return new RunAsDialogResult(newCred, null, grantSelection,
                capturedResult.CreateAppEntryOnly, capturedResult.LaunchAsLowIntegrity, capturedResult.LaunchAsSplitToken,
                capturedResult.UpdateOriginalShortcut, false, capturedResult.EditExistingApp, capturedResult.ExistingAppForLaunch);
        }

        if (capturedResult.Credential == null)
        {
            capturedResult.AdHocPassword?.Dispose();
            return empty;
        }

        return capturedResult;
    }
}