using RunFence.Core.Models;

namespace RunFence.Acl.UI.Forms;

public sealed record AclConfigValidationState(
    bool IsValid,
    AclTarget SelectedAclTarget,
    int FolderAclDepth,
    AclMode AclMode,
    bool IsAllowMode,
    bool RestrictAcl,
    string? ConflictMessage,
    string? OverlapWarning,
    string? TargetPath);
