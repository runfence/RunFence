using System.Security.AccessControl;
using RunFence.Acl.UI;

namespace RunFence.RunAs.UI;

public class RunAsAncestorPermissionPrompter : IRunAsAncestorPermissionPrompter
{
    public AncestorPermissionResult? Prompt(Form? owner, IReadOnlyList<string> ancestors)
        => AclPermissionDialogHelper.ShowAncestorPermissionDialog(
            owner,
            "Missing permissions",
            ancestors,
            FileSystemRights.ReadAndExecute);
}
