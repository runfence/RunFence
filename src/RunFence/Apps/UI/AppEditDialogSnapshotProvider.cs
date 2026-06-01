using RunFence.Core.Models;

namespace RunFence.Apps.UI;

public class AppEditDialogSnapshotProvider
{
    public AppEditDialogInputSnapshot CaptureInputSnapshot(
        IAppEditDialogSnapshotView view,
        IAppEditDialogSectionsView sectionsView)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(sectionsView);

        return new AppEditDialogInputSnapshot(
            NameText: view.AppName,
            FilePathText: view.AppPath,
            IsFolder: view.IsFolder,
            SelectedAccountSid: view.SelectedAccountSid,
            SelectedAppContainerName: view.SelectedAppContainerName,
            ManageShortcuts: view.ManageShortcuts,
            SelectedPrivilegeLevel: view.PrivilegeLevel,
            PersistedPrivilegeLevel: view.PersistedPrivilegeLevel,
            OverrideIpcCallers: view.OverrideIpcCallers,
            DefaultArgsText: view.DefaultArguments,
            AllowPassArgs: view.AllowPassingArguments,
            WorkingDirText: view.WorkingDirectory,
            AllowPassWorkDir: view.AllowPassingWorkingDirectory,
            ExistingApps: view.ExistingApps,
            ExistingApp: view.ExistingApp,
            PreGeneratedId: view.PreGeneratedId,
            ArgumentsTemplateText: view.ArgumentsTemplate,
            AppPathPrefixes: sectionsView.GetPathPrefixes(),
            DuplicateEnvironmentVariableName: sectionsView.GetFirstDuplicateEnvironmentVariableName(),
            EnvironmentVariables: sectionsView.GetEnvironmentVariables(),
            IpcCallers: view.IpcCallers,
            AclConfig: view.CaptureAclConfig(),
            HandlerMappings: sectionsView.GetAssociations(),
            IsUrlScheme: view.IsUrlScheme,
            AclTarget: view.AclTarget,
            AclMode: view.AclMode,
            RestrictAppEntryAcl: view.RestrictAppEntryAcl,
            ReplacePrefixes: view.ReplacePrefixes);
    }
}
