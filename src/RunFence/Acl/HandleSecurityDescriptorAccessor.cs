using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace RunFence.Acl;

public class HandleSecurityDescriptorAccessor(
    BackupPrivilegeSecurityDescriptorAccessor backupPrivilegeSecurityDescriptorAccessor)
    : IHandleSecurityDescriptorAccessor
{
    public FileSystemSecurity GetSecurity(SafeFileHandle handle, bool isDirectory)
        => backupPrivilegeSecurityDescriptorAccessor.ReadOwnerAndDacl(handle, isDirectory);

    public bool ModifyAclWithFallback(SafeFileHandle handle, bool isFolder, Func<FileSystemSecurity, bool> modify)
        => backupPrivilegeSecurityDescriptorAccessor.ModifyDacl(handle, isFolder, modify);

    public void SetOwnerWithFallback(SafeFileHandle handle, SecurityIdentifier ownerSid)
        => backupPrivilegeSecurityDescriptorAccessor.SetOwner(handle, ownerSid);
}
