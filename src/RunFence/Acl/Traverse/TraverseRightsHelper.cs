using System.Security.AccessControl;
using RunFence.Acl.Permissions;

namespace RunFence.Acl.Traverse;

public static class TraverseRightsHelper
{
    public static readonly FileSystemRights TraverseRights =
        FileSystemRights.Traverse | FileSystemRights.ReadAttributes | FileSystemRights.Synchronize;

    public static bool HasEffectiveTraverse(
        string dirPath, string sid, IReadOnlyList<string> groupSids,
        IAclPermissionService aclPermission)
    {
        try
        {
            if (!Directory.Exists(dirPath))
                return false;
            var security = new DirectoryInfo(dirPath).GetAccessControl();
            return aclPermission.HasEffectiveRights(security, sid, groupSids, TraverseRights);
        }
        catch
        {
            return false;
        }
    }
}