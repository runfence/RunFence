using RunFence.Core.Models;
using RunFence.Licensing;
using RunFence.RunAs.UI;

namespace RunFence.RunAs;

/// <summary>
/// Routes the RunAs dialog result to the appropriate creation or launch path.
/// Handles new account creation, new container creation, and existing credential selection.
/// </summary>
public class RunAsPostDialogRouter(
    IRunAsUserAccountCreator userAccountCreator,
    IRunAsContainerCreator containerCreator,
    IEvaluationLimitHelper evaluationLimitHelper,
    RunAsDosProtection dosProtection)
{
    /// <summary>
    /// Evaluates the dialog outcome and routes to the correct creation or selection flow.
    /// Returns the final <see cref="RunAsDialogResult"/> on success, or an empty result
    /// representing a declined or no-op outcome.
    /// </summary>
    public RunAsDialogResult Route(
        DialogResult dlgResult,
        RunAsDialogResult? capturedResult,
        bool createNewAccountRequested,
        bool createNewContainerRequested,
        string filePath,
        List<CredentialEntry> credentials)
    {
        if (dlgResult != DialogResult.OK || capturedResult == null)
        {
            dosProtection.RecordDecline();
            return RunAsDialogResult.Empty();
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
                return RunAsDialogResult.Empty();
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

            if (!evaluationLimitHelper.CheckCredentialLimit(credentials))
            {
                dosProtection.RecordDecline();
                return RunAsDialogResult.Empty();
            }

            var newCred = userAccountCreator.CreateNewAccount(filePath, out var newAccountGrant);
            if (newCred == null)
                return RunAsDialogResult.Empty();
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
            return RunAsDialogResult.Empty();
        }

        return capturedResult;
    }
}
