using RunFence.Acl.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Apps.UI;

public interface IAppEditDialogSnapshotView
{
    string? SelectedAccountSid { get; }
    string? SelectedAppContainerName { get; }
    string AppPath { get; }
    string AppName { get; }
    string DefaultArguments { get; }
    string WorkingDirectory { get; }
    AclTarget AclTarget { get; }
    AclMode AclMode { get; }
    bool IsFolder { get; }
    bool IsUrlScheme { get; }
    PrivilegeLevel? PrivilegeLevel { get; }
    PrivilegeLevel? PersistedPrivilegeLevel { get; }
    bool ReplacePrefixes { get; }
    bool ManageShortcuts { get; }
    bool RestrictAppEntryAcl { get; }
    bool OverrideIpcCallers { get; }
    bool AllowPassingArguments { get; }
    bool AllowPassingWorkingDirectory { get; }
    string? ArgumentsTemplate { get; }
    IReadOnlyList<AppEntry> ExistingApps { get; }
    AppEntry? ExistingApp { get; }
    string? PreGeneratedId { get; }
    List<string>? IpcCallers { get; }
    AclConfigSectionSnapshot CaptureAclConfig();
}
