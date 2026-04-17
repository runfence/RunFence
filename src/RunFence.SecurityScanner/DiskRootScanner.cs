using RunFence.Core.Models;

namespace RunFence.SecurityScanner;

public class DiskRootScanner(IFileSystemDataAccess dataAccess, AclCheckHelper aclCheck)
{
    public void ScanDiskRoots(ScanContext ctx)
    {
        // Exclude interactive user from disk root ACL findings — having access to drive roots
        // is expected for the desktop user and does not constitute a security risk in that context.
        var excludedSids = ctx.InteractiveUserSid != null
            ? new HashSet<string>(ctx.AdminSids, StringComparer.OrdinalIgnoreCase) { ctx.InteractiveUserSid }
            : ctx.AdminSids;

        foreach (var root in dataAccess.GetDriveRoots())
        {
            try
            {
                var security = dataAccess.GetDirectorySecurity(root);
                aclCheck.CheckContainerAcl(security, root, excludedSids,
                    StartupSecurityCategory.DiskRootAcl,
                    SecurityScanner.DiskRootCheckRightsMask,
                    ctx.Findings, ctx.Seen,
                    navigationTarget: "diskprops:" + root);
            }
            catch (Exception ex)
            {
                dataAccess.LogError($"Failed to check disk root ACL '{root}': {ex.Message}");
            }
        }
    }
}