using RunFence.Apps.UI;
using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.RunAs.UI;

namespace RunFence.RunAs;

/// <summary>
/// Handles opening and configuring AppEditDialog from the RunAs flow.
/// Extracted from RunAsAppEntryManager to reduce its responsibility scope.
/// </summary>
public class RunAsAppEditDialogHandler(
    IAppStateProvider appState,
    IAppEntryLauncher entryLauncher,
    SessionContext session,
    Func<AppEditDialog> dialogFactory,
    AppEntryPermissionPrompter permissionPrompter,
    IModalCoordinator modalCoordinator,
    IRunAsLaunchErrorHandler launchErrorHandler,
    RunAsAppShortcutCreator shortcutCreator,
    IAppEditCommitService commitService)
{
    public void OpenAppEditDialogForContainer(AppEntry? editExistingApp, string filePath,
        AppContainerEntry container, string? originalLnkPath,
        bool updateOriginalShortcut)
    {
        if (editExistingApp != null)
            OpenAppEditDialog(editExistingApp, originalLnkPath: originalLnkPath,
                updateOriginalShortcut: updateOriginalShortcut);
        else
            OpenAppEditDialog(null, filePath, null,
                originalLnkPath, updateOriginalShortcut,
                initialContainerName: container.Name);
    }

    /// <summary>
    /// Opens an AppEditDialog for a new entry (existing = null) or an existing one.
    /// For new entries, filePath and credential are required.
    /// </summary>
    public void OpenAppEditDialog(
        AppEntry? existing,
        string? filePath = null,
        CredentialEntry? credential = null,
        string? originalLnkPath = null,
        bool updateOriginalShortcut = false,
        string? initialContainerName = null,
        PrivilegeLevel? privilegeLevel = null)
    {
        modalCoordinator.BeginModal();
        try
        {
            AppEditDialogOptions options = existing != null
                ? new AppEditDialogOptions(LaunchNow: true)
                : new AppEditDialogOptions(
                    ExePath: filePath,
                    AccountSid: credential?.Sid,
                    ContainerName: initialContainerName,
                    PrivilegeLevel: privilegeLevel,
                    LaunchNow: true);

            var dlg = dialogFactory();
            var commandContext = new AppEditDialogCommandContext(
                ApplyAsync: () =>
                {
                    var permissionDecision = permissionPrompter.PromptForGrant(dlg, dlg.Result);
                    if (permissionDecision.Result == AppEntryPermissionPromptResult.Canceled)
                        throw new OperationCanceledException();

                    var commitResult = commitService.Commit(dlg.Result, existing, dlg.SelectedConfigPath);
                    if (commitResult.Status == RunAsAppEntryPersistenceStatus.Canceled)
                        throw new OperationCanceledException(commitResult.ErrorMessage);

                    if (commitResult.Status == RunAsAppEntryPersistenceStatus.SaveFailed)
                        throw new InvalidOperationException(commitResult.ErrorMessage ?? "Failed to save application.");

                    if (commitResult.Status == RunAsAppEntryPersistenceStatus.RequiredEnforcementFailed)
                    {
                        MessageBox.Show(
                            $"Application was saved, but required RunAs enforcement failed:\n\n{commitResult.WarningMessage}",
                            "Saved With Warning",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                    else if (commitResult.Status == RunAsAppEntryPersistenceStatus.ConvenienceEnforcementFailed)
                    {
                        MessageBox.Show(
                            $"Application was saved, but a convenience setup step failed:\n\n{commitResult.WarningMessage}",
                            "Saved With Warning",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }

                    if (updateOriginalShortcut && originalLnkPath != null)
                        shortcutCreator.TryUpdateOriginalShortcut(originalLnkPath, dlg.Result.Id);

                    if (permissionDecision.GrantRequest != null)
                    {
                        var grantWarning = permissionPrompter.TryApplyGrant(permissionDecision.GrantRequest);
                        if (!string.IsNullOrWhiteSpace(grantWarning))
                        {
                            MessageBox.Show(
                                $"Application was saved, but applying the selected permission grant failed:\n\n{grantWarning}",
                                "Saved With Warning",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        }
                    }

                    if (dlg.LaunchNow && commitResult.Status != RunAsAppEntryPersistenceStatus.RequiredEnforcementFailed)
                        launchErrorHandler.RunWithErrorHandling(() => entryLauncher.Launch(dlg.Result, null), dlg.Result.ExePath);
                    return Task.CompletedTask;
                });
            dlg.Initialize(
                existing,
                session.CredentialStore.Credentials.ToList(),
                appState.Database.Apps.ToList(),
                commandContext,
                options,
                appState.Database.SidNames,
                appState.Database);

            using (dlg)
            {
                dlg.StartPosition = FormStartPosition.CenterScreen;
                dlg.Shown += (_, _) => { WindowForegroundHelper.ForceToForeground(dlg.Handle); dlg.BringToFront(); };
                dlg.ShowDialog();
            }
        }
        finally
        {
            modalCoordinator.EndModal();
        }
    }
}
