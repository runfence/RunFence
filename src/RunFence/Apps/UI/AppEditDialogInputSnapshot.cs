using RunFence.Acl.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Apps.UI;

public sealed record AppEditDialogInputSnapshot(
    string NameText,
    string FilePathText,
    bool IsFolder,
    string? SelectedAccountSid,
    string? SelectedAppContainerName,
    bool ManageShortcuts,
    PrivilegeLevel? SelectedPrivilegeLevel,
    PrivilegeLevel? PersistedPrivilegeLevel,
    bool OverrideIpcCallers,
    string DefaultArgsText,
    bool AllowPassArgs,
    string WorkingDirText,
    bool AllowPassWorkDir,
    IReadOnlyList<AppEntry> ExistingApps,
    AppEntry? ExistingApp,
    string? PreGeneratedId,
    string? ArgumentsTemplateText,
    IReadOnlyList<string>? AppPathPrefixes,
    string? DuplicateEnvironmentVariableName,
    Dictionary<string, string>? EnvironmentVariables,
    List<string>? IpcCallers,
    AclConfigSectionSnapshot AclConfig,
    IReadOnlyList<HandlerAssociationItem>? HandlerMappings,
    bool IsUrlScheme,
    AclTarget AclTarget,
    AclMode AclMode,
    bool RestrictAppEntryAcl,
    bool ReplacePrefixes);
