using RunFence.Core.Models;

namespace RunFence.Acl.UI;

public sealed record AclConfigInitializationModel(
    bool RestrictAcl,
    AclMode AclMode,
    DeniedRights DeniedRights,
    IReadOnlyList<AllowAclEntry>? AllowedAclEntries,
    AclTarget AclTarget,
    int FolderAclDepth);
