using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class BackupPrivilegeSecurityDescriptorAccessorTests
{
    private static readonly SecurityIdentifier AdministratorsSid =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);

    private static readonly SecurityIdentifier UsersSid =
        new(WellKnownSidType.BuiltinUsersSid, null);

    [Fact]
    public void ModifyDacl_UsesDaclSecurityInformationOnly()
    {
        var native = new FakeBackupPrivilegeSecurityNative(CreateDescriptorBytes(isDirectory: false));
        var accessor = new BackupPrivilegeSecurityDescriptorAccessor(native);

        accessor.ModifyDacl(@"C:\test.txt", isDirectory: false, security =>
        {
            security.AddAccessRule(new FileSystemAccessRule(UsersSid, FileSystemRights.ReadData, AccessControlType.Allow));
        });

        var call = Assert.Single(native.SetSecurityInfoCalls);
        Assert.Equal(
            FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION |
            FileSecurityNative.SECURITY_INFORMATION.UNPROTECTED_DACL_SECURITY_INFORMATION,
            call.SecurityInformation);
        Assert.False(call.SecurityInformation.HasFlag(FileSecurityNative.SECURITY_INFORMATION.PROTECTED_DACL_SECURITY_INFORMATION));
        Assert.Equal(IntPtr.Zero, call.Owner);
        Assert.NotEqual(IntPtr.Zero, call.Dacl);
        var createCall = Assert.Single(native.CreateFileCalls);
        Assert.Equal(@"C:\test.txt", createCall.Path);
        Assert.Equal(
            FileSecurityNative.READ_CONTROL | FileSecurityNative.WRITE_DAC,
            createCall.DesiredAccess);
        Assert.Equal(
            FileSecurityNative.FILE_SHARE_READ | FileSecurityNative.FILE_SHARE_WRITE,
            createCall.ShareMode);
        Assert.Equal(FileSecurityNative.OPEN_EXISTING, createCall.CreationDisposition);
        Assert.Equal(FileSecurityNative.FILE_FLAG_BACKUP_SEMANTICS, createCall.FlagsAndAttributes);
    }

    [Fact]
    public void WriteOwnerAndDacl_UsesOwnerAndDaclSecurityInformation()
    {
        var native = new FakeBackupPrivilegeSecurityNative(CreateDescriptorBytes(isDirectory: true));
        var accessor = new BackupPrivilegeSecurityDescriptorAccessor(native);

        accessor.WriteOwnerAndDacl(@"C:\test", isDirectory: true, CreateSecurity(isDirectory: true));

        var call = Assert.Single(native.SetSecurityInfoCalls);
        Assert.Equal(
            FileSecurityNative.SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION |
            FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION |
            FileSecurityNative.SECURITY_INFORMATION.UNPROTECTED_DACL_SECURITY_INFORMATION,
            call.SecurityInformation);
        Assert.NotEqual(IntPtr.Zero, call.Owner);
        Assert.NotEqual(IntPtr.Zero, call.Dacl);
        var createCall = Assert.Single(native.CreateFileCalls);
        Assert.Equal(@"C:\test", createCall.Path);
        Assert.Equal(
            FileSecurityNative.READ_CONTROL | FileSecurityNative.WRITE_DAC | FileSecurityNative.WRITE_OWNER,
            createCall.DesiredAccess);
        Assert.Equal(
            FileSecurityNative.FILE_SHARE_READ | FileSecurityNative.FILE_SHARE_WRITE,
            createCall.ShareMode);
        Assert.Equal(FileSecurityNative.OPEN_EXISTING, createCall.CreationDisposition);
        Assert.Equal(FileSecurityNative.FILE_FLAG_BACKUP_SEMANTICS, createCall.FlagsAndAttributes);
    }

    [Fact]
    public void ModifyOwnerAndDacl_UsesOwnerAndDaclSecurityInformation()
    {
        var native = new FakeBackupPrivilegeSecurityNative(CreateDescriptorBytes(isDirectory: true));
        var accessor = new BackupPrivilegeSecurityDescriptorAccessor(native);

        accessor.ModifyOwnerAndDacl(@"C:\test", isDirectory: true, security =>
        {
            security.SetOwner(UsersSid);
        });

        var call = Assert.Single(native.SetSecurityInfoCalls);
        Assert.Equal(
            FileSecurityNative.SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION |
            FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION |
            FileSecurityNative.SECURITY_INFORMATION.UNPROTECTED_DACL_SECURITY_INFORMATION,
            call.SecurityInformation);
        Assert.NotEqual(IntPtr.Zero, call.Owner);
        Assert.NotEqual(IntPtr.Zero, call.Dacl);
        var createCall = Assert.Single(native.CreateFileCalls);
        Assert.Equal(@"C:\test", createCall.Path);
        Assert.Equal(
            FileSecurityNative.READ_CONTROL | FileSecurityNative.WRITE_DAC | FileSecurityNative.WRITE_OWNER,
            createCall.DesiredAccess);
        Assert.Equal(
            FileSecurityNative.FILE_SHARE_READ | FileSecurityNative.FILE_SHARE_WRITE,
            createCall.ShareMode);
        Assert.Equal(FileSecurityNative.OPEN_EXISTING, createCall.CreationDisposition);
        Assert.Equal(FileSecurityNative.FILE_FLAG_BACKUP_SEMANTICS, createCall.FlagsAndAttributes);
    }

    [Fact]
    public void ModifyDacl_ProtectedDescriptor_UsesProtectedDaclSecurityInformation()
    {
        var native = new FakeBackupPrivilegeSecurityNative(CreateDescriptorBytes(isDirectory: false, protectDacl: true));
        var accessor = new BackupPrivilegeSecurityDescriptorAccessor(native);

        accessor.ModifyDacl(@"C:\test.txt", isDirectory: false, _ => { });

        var call = Assert.Single(native.SetSecurityInfoCalls);
        Assert.Equal(
            FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION |
            FileSecurityNative.SECURITY_INFORMATION.PROTECTED_DACL_SECURITY_INFORMATION,
            call.SecurityInformation);
    }

    [Fact]
    public void ModifyOwnerAndDacl_ProtectedDescriptor_UsesProtectedDaclSecurityInformation()
    {
        var native = new FakeBackupPrivilegeSecurityNative(CreateDescriptorBytes(isDirectory: true, protectDacl: true));
        var accessor = new BackupPrivilegeSecurityDescriptorAccessor(native);

        accessor.ModifyOwnerAndDacl(@"C:\test", isDirectory: true, security =>
        {
            security.SetOwner(UsersSid);
        });

        var call = Assert.Single(native.SetSecurityInfoCalls);
        Assert.Equal(
            FileSecurityNative.SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION |
            FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION |
            FileSecurityNative.SECURITY_INFORMATION.PROTECTED_DACL_SECURITY_INFORMATION,
            call.SecurityInformation);
    }

    [Fact]
    public void WriteOwnerAndDacl_ProtectedDescriptor_UsesProtectedDaclSecurityInformation()
    {
        var native = new FakeBackupPrivilegeSecurityNative(CreateDescriptorBytes(isDirectory: true));
        var accessor = new BackupPrivilegeSecurityDescriptorAccessor(native);

        accessor.WriteOwnerAndDacl(@"C:\test", isDirectory: true, CreateSecurity(isDirectory: true, protectDacl: true));

        var call = Assert.Single(native.SetSecurityInfoCalls);
        Assert.Equal(
            FileSecurityNative.SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION |
            FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION |
            FileSecurityNative.SECURITY_INFORMATION.PROTECTED_DACL_SECURITY_INFORMATION,
            call.SecurityInformation);
    }

    [Fact]
    public void ModifyDacl_WhenSetSecurityInfoFails_ClosesHandle()
    {
        var native = new FakeBackupPrivilegeSecurityNative(CreateDescriptorBytes(isDirectory: false))
        {
            SetSecurityInfoError = 5
        };
        var accessor = new BackupPrivilegeSecurityDescriptorAccessor(native);

        var ex = Assert.Throws<Win32Exception>(() =>
            accessor.ModifyDacl(@"C:\test.txt", isDirectory: false, _ => { }));

        Assert.Equal(5, ex.NativeErrorCode);
        Assert.Equal([native.Handle], native.ClosedHandles);
    }

    [Fact]
    public void ModifyOwnerAndDacl_WhenGetSecurityDescriptorOwnerFails_ClosesHandle()
    {
        var native = new FakeBackupPrivilegeSecurityNative(CreateDescriptorBytes(isDirectory: true))
        {
            FailGetSecurityDescriptorOwner = true
        };
        var accessor = new BackupPrivilegeSecurityDescriptorAccessor(native);

        Assert.Throws<Win32Exception>(() =>
            accessor.ModifyOwnerAndDacl(@"C:\test", isDirectory: true, _ => { }));

        Assert.Equal([native.Handle], native.ClosedHandles);
    }

    [Fact]
    public void ReadOwnerAndDacl_ReleasesDescriptorMemory()
    {
        var native = new FakeBackupPrivilegeSecurityNative(CreateDescriptorBytes(isDirectory: false));
        var accessor = new BackupPrivilegeSecurityDescriptorAccessor(native);

        FileSystemSecurity security = accessor.ReadOwnerAndDacl(@"C:\test.txt", isDirectory: false);

        Assert.IsType<FileSecurity>(security);
        Assert.Single(native.LocalFreedDescriptors);
        Assert.Equal(native.AllocatedDescriptors, native.LocalFreedDescriptors);
        Assert.Equal([native.Handle], native.ClosedHandles);
    }

    [Fact]
    public void ReadOwnerAndDacl_WhenGetSecurityInfoFails_ClosesHandle()
    {
        var native = new FakeBackupPrivilegeSecurityNative(CreateDescriptorBytes(isDirectory: false))
        {
            GetSecurityInfoError = 5
        };
        var accessor = new BackupPrivilegeSecurityDescriptorAccessor(native);

        var ex = Assert.Throws<Win32Exception>(() => accessor.ReadOwnerAndDacl(@"C:\test.txt", isDirectory: false));

        Assert.Equal(5, ex.NativeErrorCode);
        Assert.Equal([native.Handle], native.ClosedHandles);
        Assert.Empty(native.LocalFreedDescriptors);
    }

    [Fact]
    public void ReadOwnerAndDacl_UsesExpectedNativeCreateFileParameters()
    {
        var native = new FakeBackupPrivilegeSecurityNative(CreateDescriptorBytes(isDirectory: false));
        var accessor = new BackupPrivilegeSecurityDescriptorAccessor(native);

        accessor.ReadOwnerAndDacl(@"C:\read.txt", isDirectory: false);

        var createCall = Assert.Single(native.CreateFileCalls);
        Assert.Equal(@"C:\read.txt", createCall.Path);
        Assert.Equal(FileSecurityNative.READ_CONTROL, createCall.DesiredAccess);
        Assert.Equal(FileSecurityNative.FILE_SHARE_READ | FileSecurityNative.FILE_SHARE_WRITE, createCall.ShareMode);
        Assert.Equal(FileSecurityNative.OPEN_EXISTING, createCall.CreationDisposition);
        Assert.Equal(FileSecurityNative.FILE_FLAG_BACKUP_SEMANTICS, createCall.FlagsAndAttributes);
    }

    private static FileSystemSecurity CreateSecurity(bool isDirectory, bool protectDacl = false)
    {
        if (isDirectory)
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(protectDacl, preserveInheritance: false);
            security.SetOwner(AdministratorsSid);
            security.AddAccessRule(new FileSystemAccessRule(
                UsersSid,
                FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            return security;
        }

        var fileSecurity = new FileSecurity();
        fileSecurity.SetAccessRuleProtection(protectDacl, preserveInheritance: false);
        fileSecurity.SetOwner(AdministratorsSid);
        fileSecurity.AddAccessRule(new FileSystemAccessRule(
            UsersSid,
            FileSystemRights.ReadAndExecute,
            AccessControlType.Allow));
        return fileSecurity;
    }

    private static byte[] CreateDescriptorBytes(bool isDirectory, bool protectDacl = false) =>
        CreateSecurity(isDirectory, protectDacl).GetSecurityDescriptorBinaryForm();

    private sealed class FakeBackupPrivilegeSecurityNative(byte[] descriptorBytes) : IBackupPrivilegeSecurityNative
    {
        public IntPtr Handle { get; } = new(1234);
        public int GetSecurityInfoError { get; set; }
        public int SetSecurityInfoError { get; set; }
        public bool FailGetSecurityDescriptorOwner { get; set; }
        public List<IntPtr> ClosedHandles { get; } = [];
        public List<IntPtr> AllocatedDescriptors { get; } = [];
        public List<IntPtr> LocalFreedDescriptors { get; } = [];
        public List<SetSecurityInfoCall> SetSecurityInfoCalls { get; } = [];
        public List<CreateFileCall> CreateFileCalls { get; } = [];

        public IntPtr CreateFile(
            string path,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile) =>
            CreateAndRecordCall(path, desiredAccess, shareMode, creationDisposition, flagsAndAttributes);

        public void CloseHandle(IntPtr handle) => ClosedHandles.Add(handle);

        public int GetSecurityInfo(
            IntPtr handle,
            FileSecurityNative.SE_OBJECT_TYPE objectType,
            FileSecurityNative.SECURITY_INFORMATION securityInformation,
            out IntPtr owner,
            out IntPtr group,
            out IntPtr dacl,
            out IntPtr sacl,
            out IntPtr securityDescriptor)
        {
            owner = IntPtr.Zero;
            group = IntPtr.Zero;
            dacl = IntPtr.Zero;
            sacl = IntPtr.Zero;
            if (GetSecurityInfoError != 0)
            {
                securityDescriptor = IntPtr.Zero;
                return GetSecurityInfoError;
            }

            securityDescriptor = Marshal.AllocHGlobal(descriptorBytes.Length);
            Marshal.Copy(descriptorBytes, 0, securityDescriptor, descriptorBytes.Length);
            AllocatedDescriptors.Add(securityDescriptor);
            return 0;
        }

        public int SetSecurityInfo(
            IntPtr handle,
            FileSecurityNative.SE_OBJECT_TYPE objectType,
            FileSecurityNative.SECURITY_INFORMATION securityInformation,
            IntPtr owner,
            IntPtr group,
            IntPtr dacl,
            IntPtr sacl)
        {
            SetSecurityInfoCalls.Add(new SetSecurityInfoCall(handle, securityInformation, owner, dacl));
            return SetSecurityInfoError;
        }

        public uint GetSecurityDescriptorLength(IntPtr securityDescriptor) =>
            FileSecurityNative.GetSecurityDescriptorLength(securityDescriptor);

        public bool GetSecurityDescriptorDacl(
            IntPtr securityDescriptor,
            out bool daclPresent,
            out IntPtr dacl,
            out bool daclDefaulted) =>
            FileSecurityNative.GetSecurityDescriptorDacl(
                securityDescriptor,
                out daclPresent,
                out dacl,
                out daclDefaulted);

        public bool GetSecurityDescriptorOwner(
            IntPtr securityDescriptor,
            out IntPtr owner,
            out bool ownerDefaulted)
        {
            if (FailGetSecurityDescriptorOwner)
            {
                owner = IntPtr.Zero;
                ownerDefaulted = false;
                return false;
            }

            return FileSecurityNative.GetSecurityDescriptorOwner(
                securityDescriptor,
                out owner,
                out ownerDefaulted);
        }

        public void LocalFree(IntPtr securityDescriptor)
        {
            LocalFreedDescriptors.Add(securityDescriptor);
            Marshal.FreeHGlobal(securityDescriptor);
        }

        private IntPtr CreateAndRecordCall(
            string path,
            uint desiredAccess,
            uint shareMode,
            uint creationDisposition,
            uint flagsAndAttributes)
        {
            CreateFileCalls.Add(new CreateFileCall(path, desiredAccess, shareMode, creationDisposition, flagsAndAttributes));
            return Handle;
        }
    }

    private sealed record SetSecurityInfoCall(
        IntPtr Handle,
        FileSecurityNative.SECURITY_INFORMATION SecurityInformation,
        IntPtr Owner,
        IntPtr Dacl);

    private sealed record CreateFileCall(
        string Path,
        uint DesiredAccess,
        uint ShareMode,
        uint CreationDisposition,
        uint FlagsAndAttributes);
}
