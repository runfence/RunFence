using System.Security.AccessControl;
using RunFence.Acl.Permissions;

namespace RunFence.Acl.Traverse;

public static class TraverseRightsHelper
{
    public static readonly FileSystemRights TraverseRights =
        FileSystemRights.Traverse | FileSystemRights.ReadAttributes | FileSystemRights.Synchronize;

    public static bool HasEffectiveTraverse(
        string dirPath, string sid, IReadOnlyList<string> groupSids,
        IAclPermissionService aclPermission,
        IFileSystemPathInfo pathInfo)
    {
        try
        {
            if (!pathInfo.DirectoryExists(dirPath))
                return false;
            var security = pathInfo.GetDirectorySecurity(dirPath);
            return aclPermission.HasEffectiveRights(security, sid, groupSids, TraverseRights);
        }
        catch
        {
            return false;
        }
    }

    public static bool HasEffectiveTraverseForGrantSid(
        string dirPath,
        string sid,
        IReadOnlyList<string> groupSids,
        IAclPermissionService aclPermission,
        IFileSystemPathInfo pathInfo)
    {
        if (HasEffectiveTraverse(dirPath, sid, groupSids, aclPermission, pathInfo))
            return true;

        return AclHelper.IsSpecificContainerSid(sid) &&
               HasEffectiveTraverse(
                   dirPath,
                   AclHelper.AllApplicationPackagesSid,
                   [],
                   aclPermission,
                   pathInfo);
    }
}
