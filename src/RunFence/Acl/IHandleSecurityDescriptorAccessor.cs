using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace RunFence.Acl;

public interface IHandleSecurityDescriptorAccessor
{
    FileSystemSecurity GetSecurity(SafeFileHandle handle, bool isDirectory);
    bool ModifyAclWithFallback(SafeFileHandle handle, bool isFolder, Func<FileSystemSecurity, bool> modify);
    void SetOwnerWithFallback(SafeFileHandle handle, SecurityIdentifier ownerSid);
}
