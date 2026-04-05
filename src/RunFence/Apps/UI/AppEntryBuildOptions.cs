using RunFence.Core.Models;

namespace RunFence.Apps.UI;

/// <summary>
/// Encapsulates all parameters for <see cref="AppEntryBuilder.Build"/>.
/// </summary>
public record AppEntryBuildOptions(
    string Name,
    string ExePath,
    bool IsFolder,
    string AccountSid,
    bool ManageShortcuts,
    string DefaultArgs,
    bool AllowPassArgs,
    string? WorkingDirectory,
    bool AllowPassWorkingDir,
    List<string>? IpcCallers,
    bool RestrictAcl,
    AclMode AclMode,
    AclTarget AclTarget,
    int FolderAclDepth,
    DeniedRights DeniedRights,
    List<AllowAclEntry>? AllowedAclEntries,
    string? ExistingId,
    DateTime? LastKnownExeTimestamp,
    string? PreGeneratedId,
    bool? LaunchAsLowIntegrity = null,
    string? AppContainerName = null,
    bool? RunAsSplitToken = null,
    Dictionary<string, string>? EnvironmentVariables = null,
    string? ArgumentsTemplate = null)
{
    /// <summary>
    /// Creates an <see cref="AppEntryBuildOptions"/> with sensible defaults for use from wizard templates.
    /// Only the parameters that vary per template need to be specified; everything else defaults to the
    /// most common case (no folder, no extra args, File ACL target, zero folder depth, etc.).
    /// </summary>
    public static AppEntryBuildOptions ForWizard(
        string name,
        string exePath,
        string accountSid,
        bool restrictAcl,
        AclMode aclMode,
        bool manageShortcuts,
        AclTarget aclTarget = AclTarget.File,
        bool? launchAsLowIntegrity = null,
        string? appContainerName = null)
        => new(
            Name: name,
            ExePath: exePath,
            IsFolder: false,
            AccountSid: accountSid,
            ManageShortcuts: manageShortcuts,
            DefaultArgs: "",
            AllowPassArgs: false,
            WorkingDirectory: null,
            AllowPassWorkingDir: false,
            IpcCallers: null,
            RestrictAcl: restrictAcl,
            AclMode: aclMode,
            AclTarget: aclTarget,
            FolderAclDepth: 0,
            DeniedRights: default,
            AllowedAclEntries: null,
            ExistingId: null,
            LastKnownExeTimestamp: null,
            PreGeneratedId: null,
            LaunchAsLowIntegrity: launchAsLowIntegrity,
            AppContainerName: appContainerName);
}