using System.Security.AccessControl;
using System.Security.Principal;

namespace RunFence.DragBridge;

public interface ITempDirectoryAclHelper
{
    void ApplyRestrictedAcl(DirectoryInfo dirInfo,
        params (IdentityReference identity, FileSystemRights rights)[] additionalRules);
}
