using RunFence.Core.Models;

namespace RunFence.Acl.UI.Forms;

/// <summary>Result of <see cref="AclConfigSection.BuildResult"/>.</summary>
public record struct AclConfigResult(
    bool RestrictAcl,
    AclMode AclMode,
    AclTarget AclTarget,
    int Depth,
    DeniedRights DeniedRights,
    List<AllowAclEntry>? AllowedEntries);
