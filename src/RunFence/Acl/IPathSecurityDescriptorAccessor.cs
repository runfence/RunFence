using System.Security.AccessControl;
using System.Security.Principal;

namespace RunFence.Acl;

public interface IPathSecurityDescriptorAccessor
{
    FileSystemSecurity GetSecurity(string path);
    string? GetOwnerSid(string path);
    bool PathExists(string path, out bool isFolder);
    bool ModifyAclWithFallback(string path, Func<FileSystemSecurity, bool> modify);
    bool ModifyOwnerAndAclWithFallback(string path, Func<FileSystemSecurity, bool> modify);
    void SetOwnerAndAclWithFallback(string path, FileSystemSecurity security);
    void SetOwnerWithFallback(string path, SecurityIdentifier ownerSid);
    void ApplyNonPropagatingAcl(string path, FileSystemSecurity security);
}
