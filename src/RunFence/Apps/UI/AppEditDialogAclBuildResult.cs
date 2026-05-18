using RunFence.Acl.UI.Forms;

namespace RunFence.Apps.UI;

public sealed record AppEditDialogAclBuildResult(
    AclConfigResult? Result,
    string? ValidationError);
