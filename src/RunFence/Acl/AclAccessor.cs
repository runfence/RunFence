using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Infrastructure;
using Microsoft.Win32.SafeHandles;

using System.ComponentModel;

namespace RunFence.Acl;

/// <summary>
/// Low-level ACL access for NTFS paths. Backup-privilege descriptor I/O is delegated to
/// <see cref="BackupPrivilegeSecurityDescriptorAccessor"/>.
/// </summary>
public class AclAccessor : IPathSecurityDescriptorAccessor, IExplicitAceAccessor
{
    private const uint DaclSecurityInformation = 0x00000004;
    private readonly IAclAccessorNative native;
    private readonly BackupPrivilegeSecurityDescriptorAccessor backupPrivilegeSecurityDescriptorAccessor;

    public AclAccessor(
        IAclAccessorNative native,
        BackupPrivilegeSecurityDescriptorAccessor backupPrivilegeSecurityDescriptorAccessor)
    {
        this.native = native;
        this.backupPrivilegeSecurityDescriptorAccessor = backupPrivilegeSecurityDescriptorAccessor;
    }

    public FileSystemSecurity GetSecurity(string path)
    {
        bool isDir = ResolvePathIsDirectory(path);
        if (isDir)
        {
            try
            {
                return new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            }
            catch (UnauthorizedAccessException)
            {
                return backupPrivilegeSecurityDescriptorAccessor.ReadOwnerAndDacl(path, isDirectory: true);
            }
        }

        try
        {
            return new FileInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        }
        catch (UnauthorizedAccessException)
        {
            return backupPrivilegeSecurityDescriptorAccessor.ReadOwnerAndDacl(path, isDirectory: false);
        }
    }

    public string? GetOwnerSid(string path)
        => (GetSecurity(path).GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier)?.Value;

    /// <summary>
    /// Removes the existing explicit ACE for this SID+type (passing <paramref name="shouldSkip"/>
    /// to preserve any ACE the caller wants to keep) and adds a new one with the given rights.
    /// If rights is zero, only removes. Other callers can omit <paramref name="shouldSkip"/>.
    /// </summary>
    public void ApplyExplicitAce(string path, string sid, AccessControlType type, FileSystemRights rights,
        Func<FileSystemAccessRule, bool>? shouldSkip = null)
    {
        var identity = new SecurityIdentifier(sid);
        bool isDir = ResolvePathIsDirectory(path);
        try
        {
            if (isDir)
            {
                var dirInfo = new DirectoryInfo(path);
                var security = dirInfo.GetAccessControl();
                RemoveExplicitAce(security, identity, type, shouldSkip);
                if (rights != 0)
                {
                    security.AddAccessRule(new FileSystemAccessRule(
                        identity,
                        rights,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        type));
                }

                dirInfo.SetAccessControl(security);
            }
            else
            {
                var fileInfo = new FileInfo(path);
                var security = fileInfo.GetAccessControl();
                RemoveExplicitAce(security, identity, type, shouldSkip);
                if (rights != 0)
                {
                    security.AddAccessRule(new FileSystemAccessRule(identity, rights, type));
                }

                fileInfo.SetAccessControl(security);
            }
        }
        catch (UnauthorizedAccessException)
        {
            backupPrivilegeSecurityDescriptorAccessor.ModifyDacl(path, isDir, security =>
            {
                RemoveExplicitAce(security, identity, type, shouldSkip);
                if (rights != 0)
                {
                    security.AddAccessRule(new FileSystemAccessRule(
                        identity,
                        rights,
                        isDir
                            ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
                            : InheritanceFlags.None,
                        PropagationFlags.None,
                        type));
                }
            });
        }
    }

    public void RemoveExplicitAces(string path, string sid, AccessControlType type,
        Func<FileSystemAccessRule, bool>? shouldSkip = null)
    {
        var identity = new SecurityIdentifier(sid);
        bool isDir = ResolvePathIsDirectory(path);
        try
        {
            if (isDir)
            {
                var dirInfo = new DirectoryInfo(path);
                var security = dirInfo.GetAccessControl();
                if (RemoveExplicitAce(security, identity, type, shouldSkip))
                {
                    dirInfo.SetAccessControl(security);
                }
            }
            else if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                var security = fileInfo.GetAccessControl();
                if (RemoveExplicitAce(security, identity, type, shouldSkip))
                {
                    fileInfo.SetAccessControl(security);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            backupPrivilegeSecurityDescriptorAccessor.ModifyDacl(
                path,
                isDir,
                security => RemoveExplicitAce(security, identity, type, shouldSkip));
        }
    }

    public bool ModifyAclWithFallback(string path, Func<FileSystemSecurity, bool> modify)
    {
        bool isFolder = ResolvePathIsDirectory(path);
        try
        {
            if (isFolder)
            {
                var dirInfo = new DirectoryInfo(path);
                var security = dirInfo.GetAccessControl();
                if (!modify(security))
                {
                    return false;
                }

                dirInfo.SetAccessControl(security);
            }
            else
            {
                var fileInfo = new FileInfo(path);
                var security = fileInfo.GetAccessControl();
                if (!modify(security))
                {
                    return false;
                }

                fileInfo.SetAccessControl(security);
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return backupPrivilegeSecurityDescriptorAccessor.ModifyDacl(path, isFolder, modify);
        }
    }

    public bool ModifyOwnerAndAclWithFallback(string path, Func<FileSystemSecurity, bool> modify)
    {
        bool isFolder = ResolvePathIsDirectory(path);
        try
        {
            if (isFolder)
            {
                var dirInfo = new DirectoryInfo(path);
                var security = dirInfo.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
                if (!modify(security))
                {
                    return false;
                }

                dirInfo.SetAccessControl((DirectorySecurity)security);
            }
            else
            {
                var fileInfo = new FileInfo(path);
                var security = fileInfo.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
                if (!modify(security))
                {
                    return false;
                }

                fileInfo.SetAccessControl((FileSecurity)security);
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            bool changed = false;
            backupPrivilegeSecurityDescriptorAccessor.ModifyOwnerAndDacl(
                path,
                isFolder,
                security => changed = modify(security));
            return changed;
        }
    }

    public void SetOwnerAndAclWithFallback(string path, FileSystemSecurity security)
    {
        bool isFolder = security switch
        {
            DirectorySecurity => true,
            FileSecurity => false,
            _ => throw new InvalidOperationException(
                $"Unsupported filesystem security type '{security.GetType().FullName}'.")
        };
        try
        {
            if (security is DirectorySecurity directorySecurity)
            {
                var dirInfo = new DirectoryInfo(path);
                dirInfo.SetAccessControl(directorySecurity);
            }
            else if (security is FileSecurity fileSecurity)
            {
                var fileInfo = new FileInfo(path);
                fileInfo.SetAccessControl(fileSecurity);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported filesystem security type '{security.GetType().FullName}'.");
            }
        }
        catch (UnauthorizedAccessException)
        {
            backupPrivilegeSecurityDescriptorAccessor.WriteOwnerAndDacl(path, isFolder, security);
        }
    }

    public void SetOwnerWithFallback(string path, SecurityIdentifier ownerSid)
    {
        _ = PathExists(path, out bool isDirectory);
        backupPrivilegeSecurityDescriptorAccessor.SetOwner(path, isDirectory, ownerSid);
    }

    public void ApplyNonPropagatingAcl(string path, FileSystemSecurity security)
    {
        var sdBytes = security.GetSecurityDescriptorBinaryForm();
        var sdHandle = GCHandle.Alloc(sdBytes, GCHandleType.Pinned);
        try
        {
            if (native.SetFileSecurity(path, DaclSecurityInformation, sdHandle.AddrOfPinnedObject()))
            {
                return;
            }

            int error = Marshal.GetLastWin32Error();
            if (error == 5)
            {
                backupPrivilegeSecurityDescriptorAccessor.WriteDaclNonPropagating(path, security);
                return;
            }

            throw new Win32Exception(
                error,
                $"SetFileSecurity failed on '{path}'");
        }
        finally
        {
            sdHandle.Free();
        }
    }

    public bool PathExists(string path, out bool isFolder)
    {
        uint attrs = FileSecurityNative.GetFileAttributes(path);
        if (attrs != FileSecurityNative.INVALID_FILE_ATTRIBUTES)
        {
            isFolder = (attrs & FileSecurityNative.FILE_ATTRIBUTE_DIRECTORY) != 0;
            return true;
        }

        int error = Marshal.GetLastWin32Error();
        if (error is 2 or 3)
        {
            isFolder = false;
            return false;
        }

        IntPtr handle = FileSecurityNative.CreateFile(path,
            0,
            FileSecurityNative.FILE_SHARE_READ | FileSecurityNative.FILE_SHARE_WRITE,
            IntPtr.Zero,
            FileSecurityNative.OPEN_EXISTING,
            FileSecurityNative.FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);

        if (handle == FileSecurityNative.INVALID_HANDLE_VALUE)
        {
            error = Marshal.GetLastWin32Error();
            isFolder = false;
            return error is not (2 or 3);
        }

        try
        {
            isFolder = FileSecurityNative.GetFileInformationByHandle(handle, out var info)
                && (info.dwFileAttributes & FileSecurityNative.FILE_ATTRIBUTE_DIRECTORY) != 0;
            return true;
        }
        finally
        {
            ProcessNative.CloseHandle(handle);
        }
    }

    private static bool RemoveExplicitAce(
        FileSystemSecurity security,
        SecurityIdentifier identity,
        AccessControlType type,
        Func<FileSystemAccessRule, bool>? shouldSkip = null)
    {
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
        bool removed = false;
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType == type &&
                rule.IdentityReference is SecurityIdentifier ruleSid &&
                ruleSid.Equals(identity))
            {
                if (shouldSkip != null && shouldSkip(rule))
                {
                    continue;
                }

                security.RemoveAccessRuleSpecific(rule);
                removed = true;
            }
        }

        return removed;
    }

    private bool ResolvePathIsDirectory(string path)
        => PathExists(path, out bool isFolder) && isFolder;
}
