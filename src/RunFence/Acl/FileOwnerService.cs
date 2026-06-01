using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Acl;

/// <summary>
/// Low-level NTFS owner operations for grant-managed paths.
/// </summary>
public class FileOwnerService(
    ILoggingService log,
    IFileSystemPathInfo pathInfo,
    IPathSecurityDescriptorAccessor aclAccessor) : IFileOwnerService
{
    public void ChangeOwner(string path, string sid, bool recursive)
    {
        var ownerSid = new SecurityIdentifier(sid);
        SetOwnerInternal(path, ownerSid);
        if (recursive && pathInfo.DirectoryExists(path))
            RecursiveSetOwner(path, ownerSid);
    }

    public void ResetOwner(string path, bool recursive)
    {
        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        SetOwnerInternal(path, adminsSid);
        if (recursive && pathInfo.DirectoryExists(path))
            RecursiveSetOwner(path, adminsSid);
    }

    private void SetOwnerInternal(string path, SecurityIdentifier ownerSid)
    {
        var security = aclAccessor.GetSecurity(path);
        security.SetOwner(ownerSid);
        aclAccessor.SetOwnerAndAclWithFallback(path, security);
    }

    private void RecursiveSetOwner(string dirPath, SecurityIdentifier ownerSid)
    {
        string[]? subDirs = null;
        try { subDirs = Directory.GetDirectories(dirPath); }
        catch (Exception ex) { log.Warn($"Failed to enumerate directories in '{dirPath}': {ex.Message}"); }

        if (subDirs != null)
        {
            foreach (var subDir in subDirs)
            {
                try
                {
                    if ((File.GetAttributes(subDir) & FileAttributes.ReparsePoint) != 0)
                        continue;
                }
                catch { continue; }
                TrySetOwner(subDir, ownerSid);
                RecursiveSetOwner(subDir, ownerSid);
            }
        }

        string[]? files = null;
        try { files = Directory.GetFiles(dirPath); }
        catch (Exception ex) { log.Warn($"Failed to enumerate files in '{dirPath}': {ex.Message}"); }

        if (files != null)
        {
            foreach (var file in files)
                TrySetOwner(file, ownerSid);
        }
    }

    private void TrySetOwner(string path, SecurityIdentifier ownerSid)
    {
        try { SetOwnerInternal(path, ownerSid); }
        catch (Exception ex) { log.Warn($"Failed to set owner on '{path}': {ex.Message}"); }
    }
}
