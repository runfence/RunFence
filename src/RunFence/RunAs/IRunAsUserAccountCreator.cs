using RunFence.Acl.UI;
using RunFence.Core.Models;

namespace RunFence.RunAs;

public interface IRunAsUserAccountCreator
{
    CredentialEntry? CreateNewAccount(string filePath, out AncestorPermissionResult? permissionGrant);
}
