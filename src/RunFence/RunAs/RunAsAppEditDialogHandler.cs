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
            dlg.Initialize(
                existing,
                session.CredentialStore.Credentials.ToList(),
                appState.Database.Apps.ToList(),
                options,
                appState.Database.SidNames,
                appState.Database);

            using (dlg)
            {
                dlg.StartPosition = FormStartPosition.CenterScreen;
                dlg.Shown += (_, _) => { WindowForegroundHelper.ForceToForeground(dlg.Handle); dlg.BringToFront(); };

                dlg.ApplyRequested += () =>
                {
                    if (!commitService.Commit(dlg.Result, existing, dlg.SelectedConfigPath))
                        return;

                    if (updateOriginalShortcut && originalLnkPath != null)
                        shortcutCreator.TryUpdateOriginalShortcut(originalLnkPath, dlg.Result.Id);

                    if (permissionPrompter.PromptAndGrant(dlg, dlg.Result))
                        commitService.SaveAllConfigs();

                    if (dlg.LaunchNow)
                        launchErrorHandler.RunWithErrorHandling(() => entryLauncher.Launch(dlg.Result, null), dlg.Result.ExePath);

                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                };

                dlg.ShowDialog();
            }
        }
        finally
        {
            modalCoordinator.EndModal();
        }
    }
}
