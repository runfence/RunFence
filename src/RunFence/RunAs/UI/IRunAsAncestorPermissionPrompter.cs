using RunFence.Acl.UI;

namespace RunFence.RunAs.UI;

public interface IRunAsAncestorPermissionPrompter
{
    AncestorPermissionResult? Prompt(Form? owner, IReadOnlyList<string> ancestors);
}
