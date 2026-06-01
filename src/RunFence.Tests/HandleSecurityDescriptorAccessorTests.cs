using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using RunFence.Acl;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class HandleSecurityDescriptorAccessorTests
{
    [Fact]
    public void ModifyAclWithFallback_Handle_RoutesThroughBackupPrivilegeAccessor()
    {
        var native = new RoutingBackupPrivilegeSecurityNative(CreateFileSecurityDescriptorBytes());
        var accessor = new HandleSecurityDescriptorAccessor(
            new BackupPrivilegeSecurityDescriptorAccessor(native));
        using var handle = new SafeFileHandle(new IntPtr(2222), ownsHandle: false);

        bool changed = accessor.ModifyAclWithFallback(handle, isFolder: false, security =>
        {
            security.SetAccessRuleProtection(false, false);
            return true;
        });

        Assert.True(changed);
        Assert.Empty(native.CreateFileCalls);
        var setSecurityInfoCall = Assert.Single(native.SetSecurityInfoCalls);
        Assert.Equal(new IntPtr(2222), setSecurityInfoCall.Handle);
        Assert.Equal(
            FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION |
            FileSecurityNative.SECURITY_INFORMATION.UNPROTECTED_DACL_SECURITY_INFORMATION,
            setSecurityInfoCall.SecurityInformation);
        Assert.Equal(IntPtr.Zero, setSecurityInfoCall.Owner);
        Assert.NotEqual(IntPtr.Zero, setSecurityInfoCall.Dacl);
        Assert.Equal(IntPtr.Zero, setSecurityInfoCall.Group);
        Assert.Equal(IntPtr.Zero, setSecurityInfoCall.Sacl);
    }

    [Fact]
    public void SetOwnerWithFallback_Handle_RoutesThroughBackupPrivilegeAccessor()
    {
        var native = new RoutingBackupPrivilegeSecurityNative(CreateFileSecurityDescriptorBytes());
        var accessor = new HandleSecurityDescriptorAccessor(
            new BackupPrivilegeSecurityDescriptorAccessor(native));
        using var handle = new SafeFileHandle(new IntPtr(3333), ownsHandle: false);
        var ownerSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        accessor.SetOwnerWithFallback(handle, ownerSid);

        Assert.Empty(native.CreateFileCalls);
        var setSecurityInfoCall = Assert.Single(native.SetSecurityInfoCalls);
        Assert.Equal(new IntPtr(3333), setSecurityInfoCall.Handle);
        Assert.Equal(
            FileSecurityNative.SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION,
            setSecurityInfoCall.SecurityInformation);
        Assert.NotEqual(IntPtr.Zero, setSecurityInfoCall.Owner);
        Assert.Equal(IntPtr.Zero, setSecurityInfoCall.Dacl);
        Assert.Equal(IntPtr.Zero, setSecurityInfoCall.Group);
        Assert.Equal(IntPtr.Zero, setSecurityInfoCall.Sacl);
        Assert.Empty(native.ClosedHandles);
    }

    private static byte[] CreateFileSecurityDescriptorBytes()
    {
        var security = new FileSecurity();
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.ReadAndExecute,
            AccessControlType.Allow));
        return security.GetSecurityDescriptorBinaryForm();
    }

    private sealed class RoutingBackupPrivilegeSecurityNative(byte[] securityDescriptorBytes) : IBackupPrivilegeSecurityNative
    {
        public IntPtr Handle { get; } = new(1234);
        public int GetSecurityInfoError { get; set; }
        public int SetSecurityInfoError { get; set; }
        public List<SetFileCall> CreateFileCalls { get; } = [];
        public List<SetSecurityInfoCall> SetSecurityInfoCalls { get; } = [];
        public List<IntPtr> ClosedHandles { get; } = [];

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
            out IntPtr descriptor)
        {
            owner = IntPtr.Zero;
            group = IntPtr.Zero;
            dacl = IntPtr.Zero;
            sacl = IntPtr.Zero;
            if (GetSecurityInfoError != 0)
            {
                descriptor = IntPtr.Zero;
                return GetSecurityInfoError;
            }

            descriptor = Marshal.AllocHGlobal(securityDescriptorBytes.Length);
            Marshal.Copy(securityDescriptorBytes, 0, descriptor, securityDescriptorBytes.Length);
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
            SetSecurityInfoCalls.Add(new SetSecurityInfoCall(handle, securityInformation, owner, group, dacl, sacl));
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
            if (securityDescriptor == IntPtr.Zero)
            {
                owner = IntPtr.Zero;
                ownerDefaulted = false;
                return false;
            }

            return FileSecurityNative.GetSecurityDescriptorOwner(securityDescriptor, out owner, out ownerDefaulted);
        }

        public void LocalFree(IntPtr securityDescriptor)
            => Marshal.FreeHGlobal(securityDescriptor);

        private IntPtr CreateAndRecordCall(
            string path,
            uint desiredAccess,
            uint shareMode,
            uint creationDisposition,
            uint flagsAndAttributes) =>
            RecordCall(new(path, desiredAccess, shareMode, creationDisposition, flagsAndAttributes));

        private IntPtr RecordCall(SetFileCall call)
        {
            CreateFileCalls.Add(call);
            return Handle;
        }
    }

    private sealed record SetFileCall(
        string Path,
        uint DesiredAccess,
        uint ShareMode,
        uint CreationDisposition,
        uint FlagsAndAttributes);

    private sealed record SetSecurityInfoCall(
        IntPtr Handle,
        FileSecurityNative.SECURITY_INFORMATION SecurityInformation,
        IntPtr Owner,
        IntPtr Group,
        IntPtr Dacl,
        IntPtr Sacl);
}
