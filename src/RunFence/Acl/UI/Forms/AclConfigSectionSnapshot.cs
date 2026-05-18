using RunFence.Core.Models;

namespace RunFence.Acl.UI.Forms;

public sealed record AclConfigSectionSnapshot(
    bool RestrictAcl,
    AclMode AclMode,
    AclTarget SelectedAclTarget,
    int FolderAclDepth,
    DeniedRights DeniedRights,
    IReadOnlyList<AllowAclEntry> AllowedEntries);
