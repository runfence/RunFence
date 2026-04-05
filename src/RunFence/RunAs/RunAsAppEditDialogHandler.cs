using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence;
using RunFence.RunAs.UI;
using RunFence.UI.Forms;

namespace RunFence.RunAs;

/// <summary>
/// Handles opening and configuring AppEditDialog from the RunAs flow.
/// Extracted from RunAsAppEntryManager to reduce its responsibility scope.
/// </summary>
public class RunAsAppEditDialogHandler(
    IAppStateProvider appState,
    IDataChangeNotifier dataChangeNotifier,
    IAppLaunchOrchestrator launchOrchestrator,
    ILoggingService log,
    SessionContext session,
    IAppConfigService appConfigService,
    Func<AppEditDialog> dialogFactory,
    AppEntryPermissionPrompter permissionPrompter,
    RunAsAppEntryManager appEntryManager)
{
    public void OpenAppEditDialogForContainer(AppEntry? editExistingApp, string filePath,
        AppContainerEntry container, bool launchAsLowIntegrity, string? originalLnkPath,
        bool updateOriginalShortcut)
    {
        if (editExistingApp != null)
            OpenAppEditDialog(editExistingApp, originalLnkPath: originalLnkPath,
                updateOriginalShortcut: updateOriginalShortcut);
        else
            OpenAppEditDialog(null, filePath, null, launchAsLowIntegrity,
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
        bool? launchAsLowIntegrity = null,
        string? originalLnkPath = null,
        bool updateOriginalShortcut = false,
        string? initialContainerName = null,
        bool? launchAsSplitToken = null)
    {
        var originalConfigPath = existing != null
            ? appConfigService.GetConfigPath(existing.Id)
            : null;

        DataPanel.BeginModal();
        try
        {
            AppEditDialogOptions options = existing != null
                ? new AppEditDialogOptions(LaunchNow: true)
                : new AppEditDialogOptions(
                    ExePath: filePath,
                    AccountSid: credential?.Sid,
                    ContainerName: initialContainerName,
                    LaunchAsLowIl: launchAsLowIntegrity,
                    RunAsSplitToken: launchAsSplitToken,
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
                dlg.Shown += (_, _) => NativeInterop.ForceToForeground(dlg);

                dlg.ApplyRequested += () =>
                {
                    if (existing != null)
                    {
                        var index = appState.Database.Apps.FindIndex(a => a.Id == existing.Id);
                        if (index < 0)
                            return;

                        appEntryManager.RevertAppChanges(existing);
                        appState.Database.Apps[index] = dlg.Result;
                        appConfigService.AssignApp(dlg.Result.Id, dlg.SelectedConfigPath);
                        try
                        {
                            appEntryManager.ApplyAppChanges(dlg.Result);
                            using var scope = session.PinDerivedKey.Unprotect();
                            appConfigService.SaveAllConfigs(appState.Database, scope.Data,
                                session.CredentialStore.ArgonSalt);
                        }
                        catch
                        {
                            appState.Database.Apps[index] = existing;
                            appConfigService.AssignApp(existing.Id, originalConfigPath);
                            try
                            {
                                appEntryManager.ApplyAppChanges(existing);
                            }
                            catch (Exception restoreEx)
                            {
                                log.Error("Failed to restore ACL after edit failure", restoreEx);
                            }

                            throw;
                        }

                        if (updateOriginalShortcut && originalLnkPath != null)
                            appEntryManager.TryUpdateOriginalShortcut(originalLnkPath, dlg.Result.Id);

                        try
                        {
                            dataChangeNotifier.NotifyDataChanged();
                        }
                        catch (Exception ex)
                        {
                            log.Warn($"Failed to refresh UI: {ex.Message}");
                        }
                    }
                    else
                    {
                        if (!appEntryManager.PersistNewAppEntry(dlg.Result, dlg.SelectedConfigPath))
                            return;

                        if (updateOriginalShortcut && originalLnkPath != null)
                            appEntryManager.TryUpdateOriginalShortcut(originalLnkPath, dlg.Result.Id);
                    }

                    if (permissionPrompter.PromptAndGrant(dlg, dlg.Result))
                    {
                        using var traverseScope = session.PinDerivedKey.Unprotect();
                        appConfigService.SaveAllConfigs(appState.Database, traverseScope.Data,
                            session.CredentialStore.ArgonSalt);
                    }

                    if (dlg.LaunchNow)
                        appEntryManager.RunWithLaunchErrorHandling(() => launchOrchestrator.Launch(dlg.Result, null), dlg.Result.ExePath);

                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                };

                dlg.ShowDialog();
            }
        }
        finally
        {
            DataPanel.EndModal();
        }
    }
}