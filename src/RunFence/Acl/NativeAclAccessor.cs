using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl.Traverse;
using RunFence.Infrastructure;

namespace RunFence.Acl;

/// <summary>
/// Low-level native security I/O for NTFS paths: reads security descriptors via backup privilege
/// fallback and applies/removes explicit ACEs.
/// SeBackupPrivilege, SeRestorePrivilege, and SeTakeOwnershipPrivilege are enabled once at
/// startup (Program.cs) for the lifetime of the elevated admin process — no per-call enable needed.
/// </summary>
public static class NativeAclAccessor
{
    public static FileSystemSecurity GetSecurity(string path)
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
    private static void ModifyDaclViaBackupPrivilege(string path, bool isDir, Action<FileSystemSecurity> modifier)
    {
        IntPtr handle = NativeMethods.CreateFile(path,
            NativeMethods.READ_CONTROL | NativeMethods.WRITE_DAC,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (handle == NativeMethods.INVALID_HANDLE_VALUE)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var security = ReadSecurityFromHandle(handle,
                NativeMethods.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION, isDir);
            modifier(security);

            byte[] modifiedBytes = security.GetSecurityDescriptorBinaryForm();
            var pinned = GCHandle.Alloc(modifiedBytes, GCHandleType.Pinned);
            try
            {
                if (!NativeMethods.GetSecurityDescriptorDacl(pinned.AddrOfPinnedObject(),
                        out bool daclPresent, out IntPtr pDacl, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                int err = NativeMethods.SetSecurityInfo(handle,
                    NativeMethods.SE_OBJECT_TYPE.SE_FILE_OBJECT,
                    NativeMethods.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION,
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
            NativeMethods.CloseHandle(handle);
        }
    }

    /// <summary>
    /// Removes the existing explicit ACE for this SID+type and adds a new one with the given rights.
    /// If rights is zero, only removes.
    /// </summary>
    public static void ApplyExplicitAce(string path, string sid, AccessControlType type, FileSystemRights rights)
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

    public static void RemoveExplicitAces(string path, string sid, AccessControlType type)
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

    private static FileSystemSecurity GetSecurityViaBackupPrivilege(string path, bool isDir)
    {
        IntPtr handle = NativeMethods.CreateFile(path,
            NativeMethods.READ_CONTROL,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (handle == NativeMethods.INVALID_HANDLE_VALUE)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            return ReadSecurityFromHandle(handle,
                NativeMethods.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION |
                NativeMethods.SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION,
                isDir);
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static FileSystemSecurity ReadSecurityFromHandle(
        IntPtr handle, NativeMethods.SECURITY_INFORMATION info, bool isDir)
    {
        int error = NativeMethods.GetSecurityInfo(handle,
            NativeMethods.SE_OBJECT_TYPE.SE_FILE_OBJECT, info,
            out _, out _, out _, out _, out IntPtr pSecDesc);
        if (error != 0)
            throw new Win32Exception(error);
        try
        {
            int length = (int)NativeMethods.GetSecurityDescriptorLength(pSecDesc);
            var bytes = new byte[length];
            Marshal.Copy(pSecDesc, bytes, 0, length);
            FileSystemSecurity security = isDir ? new DirectorySecurity() : new FileSecurity();
            security.SetSecurityDescriptorBinaryForm(bytes);
            return security;
        }
        finally
        {
            NativeMethods.LocalFree(pSecDesc);
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