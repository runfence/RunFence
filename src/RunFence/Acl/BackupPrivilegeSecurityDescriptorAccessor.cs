using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;
using RunFence.Infrastructure;

namespace RunFence.Acl;

/// <summary>
/// Reads and writes owner/DACL security descriptors using backup semantics handles so callers do
/// not duplicate the handle lifetime and descriptor marshaling path.
/// </summary>
public class BackupPrivilegeSecurityDescriptorAccessor(IBackupPrivilegeSecurityNative native)
{
    public FileSystemSecurity ReadOwnerAndDacl(string path, bool isDirectory)
    {
        IntPtr handle = OpenHandle(path, FileSecurityNative.READ_CONTROL);
        try
        {
            return ReadSecurityFromHandle(
                handle,
                FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION |
                FileSecurityNative.SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION,
                isDirectory);
        }
        finally
        {
            native.CloseHandle(handle);
        }
    }

    public void ModifyDacl(string path, bool isDirectory, Action<FileSystemSecurity> modifier)
    {
        IntPtr handle = OpenHandle(path, FileSecurityNative.READ_CONTROL | FileSecurityNative.WRITE_DAC);
        try
        {
            var security = ReadSecurityFromHandle(
                handle,
                FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION,
                isDirectory);
            modifier(security);
            WriteSecurityInfo(
                handle,
                security,
                FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION,
                includeOwner: false);
        }
        finally
        {
            native.CloseHandle(handle);
        }
    }

    public void ModifyOwnerAndDacl(string path, bool isDirectory, Action<FileSystemSecurity> modifier)
    {
        IntPtr handle = OpenHandle(
            path,
            FileSecurityNative.READ_CONTROL | FileSecurityNative.WRITE_DAC | FileSecurityNative.WRITE_OWNER);
        try
        {
            var security = ReadSecurityFromHandle(
                handle,
                FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION |
                FileSecurityNative.SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION,
                isDirectory);
            modifier(security);
            WriteSecurityInfo(
                handle,
                security,
                FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION |
                FileSecurityNative.SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION,
                includeOwner: true);
        }
        finally
        {
            native.CloseHandle(handle);
        }
    }

    public void WriteOwnerAndDacl(string path, bool isDirectory, FileSystemSecurity security)
    {
        IntPtr handle = OpenHandle(
            path,
            FileSecurityNative.READ_CONTROL | FileSecurityNative.WRITE_DAC | FileSecurityNative.WRITE_OWNER);
        try
        {
            WriteSecurityInfo(
                handle,
                security,
                FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION |
                FileSecurityNative.SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION,
                includeOwner: true);
        }
        finally
        {
            native.CloseHandle(handle);
        }
    }

    public void WriteOwnerAndDacl(SafeFileHandle handle, FileSystemSecurity security)
    {
        WriteSecurityInfo(
            handle.DangerousGetHandle(),
            security,
            FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION |
            FileSecurityNative.SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION,
            includeOwner: true);
    }

    private IntPtr OpenHandle(string path, uint desiredAccess)
    {
        IntPtr handle = native.CreateFile(
            path,
            desiredAccess,
            FileSecurityNative.FILE_SHARE_READ | FileSecurityNative.FILE_SHARE_WRITE,
            IntPtr.Zero,
            FileSecurityNative.OPEN_EXISTING,
            FileSecurityNative.FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);
        if (handle == FileSecurityNative.INVALID_HANDLE_VALUE)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return handle;
    }

    private FileSystemSecurity ReadSecurityFromHandle(
        IntPtr handle,
        FileSecurityNative.SECURITY_INFORMATION securityInformation,
        bool isDirectory)
    {
        int error = native.GetSecurityInfo(
            handle,
            FileSecurityNative.SE_OBJECT_TYPE.SE_FILE_OBJECT,
            securityInformation,
            out _,
            out _,
            out _,
            out _,
            out IntPtr securityDescriptor);
        if (error != 0)
        {
            throw new Win32Exception(error);
        }

        try
        {
            int length = (int)native.GetSecurityDescriptorLength(securityDescriptor);
            var bytes = new byte[length];
            Marshal.Copy(securityDescriptor, bytes, 0, length);

            FileSystemSecurity security = isDirectory ? new DirectorySecurity() : new FileSecurity();
            security.SetSecurityDescriptorBinaryForm(bytes);
            return security;
        }
        finally
        {
            native.LocalFree(securityDescriptor);
        }
    }

    private void WriteSecurityInfo(
        IntPtr handle,
        FileSystemSecurity security,
        FileSecurityNative.SECURITY_INFORMATION securityInformation,
        bool includeOwner)
    {
        byte[] securityDescriptorBytes = security.GetSecurityDescriptorBinaryForm();
        var pinned = GCHandle.Alloc(securityDescriptorBytes, GCHandleType.Pinned);
        try
        {
            IntPtr securityDescriptor = pinned.AddrOfPinnedObject();
            if (!native.GetSecurityDescriptorDacl(
                    securityDescriptor,
                    out bool daclPresent,
                    out IntPtr dacl,
                    out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            IntPtr owner = IntPtr.Zero;
            if (includeOwner &&
                !native.GetSecurityDescriptorOwner(securityDescriptor, out owner, out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (securityInformation.HasFlag(FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION))
            {
                securityInformation &= ~FileSecurityNative.SECURITY_INFORMATION.PROTECTED_DACL_SECURITY_INFORMATION;
                securityInformation &= ~FileSecurityNative.SECURITY_INFORMATION.UNPROTECTED_DACL_SECURITY_INFORMATION;
                securityInformation |= security.AreAccessRulesProtected
                    ? FileSecurityNative.SECURITY_INFORMATION.PROTECTED_DACL_SECURITY_INFORMATION
                    : FileSecurityNative.SECURITY_INFORMATION.UNPROTECTED_DACL_SECURITY_INFORMATION;
            }
            int error = native.SetSecurityInfo(
                handle,
                FileSecurityNative.SE_OBJECT_TYPE.SE_FILE_OBJECT,
                securityInformation,
                owner,
                IntPtr.Zero,
                daclPresent ? dacl : IntPtr.Zero,
                IntPtr.Zero);
            if (error != 0)
            {
                throw new Win32Exception(error);
            }
        }
        finally
        {
            pinned.Free();
        }
    }
}
