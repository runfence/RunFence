using RunFence.Core.Models;

namespace RunFence.SecurityScanner;

public class DiskRootScanner
{
    private readonly IFileSystemDataAccess _dataAccess;
    private readonly AclCheckHelper _aclCheck;

    public DiskRootScanner(IFileSystemDataAccess dataAccess, AclCheckHelper aclCheck)
    {
        _dataAccess = dataAccess;
        _aclCheck = aclCheck;
    }

    public void ScanDiskRoots(ScanContext ctx)
    {
        // Exclude interactive user from disk root ACL findings — having access to drive roots
        // is expected for the desktop user and does not constitute a security risk in that context.
        var excludedSids = ctx.InteractiveUserSid != null
            ? new HashSet<string>(ctx.AdminSids, StringComparer.OrdinalIgnoreCase) { ctx.InteractiveUserSid }
            : ctx.AdminSids;

        foreach (var root in _dataAccess.GetDriveRoots())
        {
            try
            {
                var security = _dataAccess.GetDirectorySecurity(root);
                _aclCheck.CheckContainerAcl(security, root, excludedSids,
                    StartupSecurityCategory.DiskRootAcl,
                    SecurityScanner.DiskRootCheckRightsMask,
                    ctx.Findings, ctx.Seen,
                    navigationTarget: "diskprops:" + root);
            }
            catch (Exception ex)
            {
                _dataAccess.LogError($"Failed to check disk root ACL '{root}': {ex.Message}");
            }
        }
    }
}