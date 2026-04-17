using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl.Traverse;
using RunFence.Infrastructure;

namespace RunFence.Acl;

public interface IAclAccessor
{
    FileSystemSecurity GetSecurity(string path);
    void ApplyExplicitAce(string path, string sid, AccessControlType type, FileSystemRights rights);
    void RemoveExplicitAces(string path, string sid, AccessControlType type);

    /// <summary>
    /// Returns true if the path exists. When <see cref="GetFileAttributes"/> fails with an
    /// access-denied error, falls back to <c>CreateFile</c> with
    /// <c>FILE_FLAG_BACKUP_SEMANTICS</c> so that paths inaccessible to normal callers but
    /// reachable via the process's backup/restore privilege are correctly detected.
    /// Sets <paramref name="isFolder"/> when the path is a directory.
    /// </summary>
    bool PathExists(string path, out bool isFolder);
}

/// <summary>
/// Low-level native security I/O for NTFS paths: reads security descriptors via backup privilege
/// fallback and applies/removes explicit ACEs.
/// SeBackupPrivilege, SeRestorePrivilege, and SeTakeOwnershipPrivilege are enabled once at
/// startup (Program.cs) for the lifetime of the elevated admin process — no per-call enable needed.
/// </summary>
public class AclAccessor : IAclAccessor
{
    public FileSystemSecurity GetSecurity(string path)
    {
        if (Directory.Exists(path))
        {
            try
            {
                return new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            }
            catch (UnauthorizedAccessException)
            {
                return GetSecurityViaBackupPrivilege(path, isDir: true);
            }
        }

        try
        {
            return new FileInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        }
        catch (UnauthorizedAccessException)
        {
            return GetSecurityViaBackupPrivilege(path, isDir: false);
        }
    }

    /// <summary>
    /// Reads, modifies, and writes the DACL bypassing DACL restrictions via SeBackupPrivilege + SeRestorePrivilege.
    /// Called only when the standard GetAccessControl/SetAccessControl path fails with UnauthorizedAccessException.
    /// </summary>
    private void ModifyDaclViaBackupPrivilege(string path, bool isDir, Action<FileSystemSecurity> modifier)
    {
        IntPtr handle = FileSecurityNative.CreateFile(path,
            FileSecurityNative.READ_CONTROL | FileSecurityNative.WRITE_DAC,
            FileSecurityNative.FILE_SHARE_READ | FileSecurityNative.FILE_SHARE_WRITE,
            IntPtr.Zero, FileSecurityNative.OPEN_EXISTING,
            FileSecurityNative.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (handle == FileSecurityNative.INVALID_HANDLE_VALUE)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var security = ReadSecurityFromHandle(handle,
                FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION, isDir);
            modifier(security);

            byte[] modifiedBytes = security.GetSecurityDescriptorBinaryForm();
            var pinned = GCHandle.Alloc(modifiedBytes, GCHandleType.Pinned);
            try
            {
                if (!FileSecurityNative.GetSecurityDescriptorDacl(pinned.AddrOfPinnedObject(),
                        out bool daclPresent, out IntPtr pDacl, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                int err = FileSecurityNative.SetSecurityInfo(handle,
                    FileSecurityNative.SE_OBJECT_TYPE.SE_FILE_OBJECT,
                    FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION,
                    IntPtr.Zero, IntPtr.Zero,
                    daclPresent ? pDacl : IntPtr.Zero, IntPtr.Zero);
                if (err != 0)
                    throw new Win32Exception(err);
            }
            finally
            {
                pinned.Free();
            }
        }
        finally
        {
            ProcessNative.CloseHandle(handle);
        }
    }

    /// <summary>
    /// Removes the existing explicit ACE for this SID+type and adds a new one with the given rights.
    /// If rights is zero, only removes.
    /// </summary>
    public void ApplyExplicitAce(string path, string sid, AccessControlType type, FileSystemRights rights)
    {
        var identity = new SecurityIdentifier(sid);
        bool isDir = Directory.Exists(path);
        try
        {
            if (isDir)
            {
                var dirInfo = new DirectoryInfo(path);
                var security = dirInfo.GetAccessControl();
                RemoveExplicitAce(security, identity, type);
                if (rights != 0)
                    security.AddAccessRule(new FileSystemAccessRule(
                        identity, rights,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None, type));
                dirInfo.SetAccessControl(security);
            }
            else
            {
                var fileInfo = new FileInfo(path);
                var security = fileInfo.GetAccessControl();
                RemoveExplicitAce(security, identity, type);
                if (rights != 0)
                    security.AddAccessRule(new FileSystemAccessRule(identity, rights, type));
                fileInfo.SetAccessControl(security);
            }
        }
        catch (UnauthorizedAccessException)
        {
            ModifyDaclViaBackupPrivilege(path, isDir, security =>
            {
                RemoveExplicitAce(security, identity, type);
                if (rights != 0)
                    security.AddAccessRule(new FileSystemAccessRule(
                        identity, rights,
                        isDir ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None,
                        PropagationFlags.None, type));
            });
        }
    }

    public void RemoveExplicitAces(string path, string sid, AccessControlType type)
    {
        var identity = new SecurityIdentifier(sid);
        bool isDir = Directory.Exists(path);
        try
        {
            if (isDir)
            {
                var dirInfo = new DirectoryInfo(path);
                var security = dirInfo.GetAccessControl();
                if (RemoveExplicitAce(security, identity, type))
                    dirInfo.SetAccessControl(security);
            }
            else if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                var security = fileInfo.GetAccessControl();
                if (RemoveExplicitAce(security, identity, type))
                    fileInfo.SetAccessControl(security);
            }
        }
        catch (UnauthorizedAccessException)
        {
            ModifyDaclViaBackupPrivilege(path, isDir, security => RemoveExplicitAce(security, identity, type));
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
        if (error is 2 or 3) // ERROR_FILE_NOT_FOUND, ERROR_PATH_NOT_FOUND
        {
            isFolder = false;
            return false;
        }

        // Access denied (or another unexpected error) — retry via backup privilege.
        IntPtr handle = FileSecurityNative.CreateFile(path,
            FileSecurityNative.READ_CONTROL,
            FileSecurityNative.FILE_SHARE_READ | FileSecurityNative.FILE_SHARE_WRITE,
            IntPtr.Zero, FileSecurityNative.OPEN_EXISTING,
            FileSecurityNative.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);

        if (handle == FileSecurityNative.INVALID_HANDLE_VALUE)
        {
            error = Marshal.GetLastWin32Error();
            isFolder = false;
            return error is not (2 or 3); // non-not-found error → treat as possibly existent
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

    private FileSystemSecurity GetSecurityViaBackupPrivilege(string path, bool isDir)
    {
        IntPtr handle = FileSecurityNative.CreateFile(path,
            FileSecurityNative.READ_CONTROL,
            FileSecurityNative.FILE_SHARE_READ | FileSecurityNative.FILE_SHARE_WRITE,
            IntPtr.Zero, FileSecurityNative.OPEN_EXISTING,
            FileSecurityNative.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (handle == FileSecurityNative.INVALID_HANDLE_VALUE)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            return ReadSecurityFromHandle(handle,
                FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION |
                FileSecurityNative.SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION,
                isDir);
        }
        finally
        {
            ProcessNative.CloseHandle(handle);
        }
    }

    private FileSystemSecurity ReadSecurityFromHandle(
        IntPtr handle, FileSecurityNative.SECURITY_INFORMATION info, bool isDir)
    {
        int error = FileSecurityNative.GetSecurityInfo(handle,
            FileSecurityNative.SE_OBJECT_TYPE.SE_FILE_OBJECT, info,
            out _, out _, out _, out _, out IntPtr pSecDesc);
        if (error != 0)
            throw new Win32Exception(error);
        try
        {
            int length = (int)FileSecurityNative.GetSecurityDescriptorLength(pSecDesc);
            var bytes = new byte[length];
            Marshal.Copy(pSecDesc, bytes, 0, length);
            FileSystemSecurity security = isDir ? new DirectorySecurity() : new FileSecurity();
            security.SetSecurityDescriptorBinaryForm(bytes);
            return security;
        }
        finally
        {
            ProcessNative.LocalFree(pSecDesc);
        }
    }

    private static bool RemoveExplicitAce(FileSystemSecurity security, SecurityIdentifier identity, AccessControlType type)
    {
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
        bool removed = false;
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType == type &&
                rule.IdentityReference is SecurityIdentifier ruleSid &&
                ruleSid.Equals(identity))
            {
                // Preserve traverse-only ACEs — managed by the traverse system, not the grant system.
                // Removing them here would wipe traverse access co-located with a grant entry.
                if (type == AccessControlType.Allow &&
                    rule.FileSystemRights == TraverseRightsHelper.TraverseRights &&
                    rule.InheritanceFlags == InheritanceFlags.None)
                    continue;

                security.RemoveAccessRuleSpecific(rule);
                removed = true;
            }
        }

        return removed;
    }
}