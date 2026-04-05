using System.Security.AccessControl;
using System.Security.Principal;

namespace RunFence.DragBridge;

/// <summary>
/// Applies a restricted ACL to a temp directory: breaks inheritance, grants Admins and
/// the current user full control, then adds caller-specified additional rules.
/// </summary>
public static class TempDirectoryAclHelper
{
    private static readonly SecurityIdentifier AdminSid =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);

    public static void ApplyRestrictedAcl(DirectoryInfo dirInfo,
        params (IdentityReference identity, FileSystemRights rights)[] additionalRules)
    {
        var currentUser = WindowsIdentity.GetCurrent().User;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);

        if (currentUser != null)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                currentUser, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        }

        security.AddAccessRule(new FileSystemAccessRule(
            AdminSid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));

        foreach (var (identity, rights) in additionalRules)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                identity, rights,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        }

        dirInfo.SetAccessControl(security);
    }
}