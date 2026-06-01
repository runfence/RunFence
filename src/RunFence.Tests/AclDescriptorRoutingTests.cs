using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.InteropServices;
using Moq;
using RunFence.Acl;
using RunFence.Infrastructure;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class AclDescriptorRoutingTests
{
    private const string TestPath = @"C:\Acl\Routing";
    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

    [Fact]
    public void DriveAclReplacer_ReplaceDriveAcl_UsesDescriptorAccessorMutation()
    {
        var security = new DirectorySecurity();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.WorldSid, null));

        var accessor = new FakeDescriptorAccessor(TestPath, security);
        var pathGrantService = new Mock<IGrantSyncService>();
        var replacer = new DriveAclReplacer(pathGrantService.Object, Mock.Of<ILoggingService>(), accessor);

        var error = replacer.ReplaceDriveAcl(TestPath, TestSid);

        Assert.Null(error);
        Assert.Equal(1, accessor.ModifyOwnerAndAclCalls);
        var storedSecurity = Assert.IsType<DirectorySecurity>(accessor.GetStoredSecurity());
        var accessRules = storedSecurity.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToList();
        Assert.DoesNotContain(accessRules, rule => ((SecurityIdentifier)rule.IdentityReference).IsWellKnown(WellKnownSidType.WorldSid));
        var targetRule = Assert.Single(accessRules, rule =>
            ((SecurityIdentifier)rule.IdentityReference).Value == TestSid);
        Assert.Equal(
            FileSystemRights.ReadAndExecute,
            targetRule.FileSystemRights & FileSystemRights.ReadAndExecute);
        Assert.Equal(
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            targetRule.InheritanceFlags);
        Assert.Equal(PropagationFlags.None, targetRule.PropagationFlags);
        Assert.Equal(AccessControlType.Allow, targetRule.AccessControlType);
        Assert.Equal(TestSid, ((SecurityIdentifier)storedSecurity.GetOwner(typeof(SecurityIdentifier))!).Value);
        pathGrantService.Verify(service => service.UpdateFromPath(TestPath, TestSid), Times.Once);
    }

    [Fact]
    public void DriveAclReplacer_HasReplaceableBroadAces_UsesDescriptorAccessorRead()
    {
        var security = new DirectorySecurity();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        var accessor = new FakeDescriptorAccessor(TestPath, security);
        var replacer = new DriveAclReplacer(Mock.Of<IGrantSyncService>(), Mock.Of<ILoggingService>(), accessor);

        Assert.True(replacer.HasReplaceableBroadAces(TestPath));
        Assert.Equal(1, accessor.GetSecurityCalls);
    }

    [Fact]
    public void FileOwnerService_ChangeOwner_UsesDescriptorAccessorWrite()
    {
        var security = new FileSecurity();
        var accessor = new FakeDescriptorAccessor(TestPath, security);
        var pathInfo = new TestFileSystemPathInfo().AddFile(TestPath);
        var service = new FileOwnerService(Mock.Of<ILoggingService>(), pathInfo, accessor);

        service.ChangeOwner(TestPath, TestSid, recursive: false);

        Assert.Equal(1, accessor.SetOwnerAndAclCalls);
        Assert.Equal(TestSid, accessor.StoredOwnerSid);
        Assert.Equal(TestSid, ((SecurityIdentifier)accessor.GetStoredSecurity().GetOwner(typeof(SecurityIdentifier))!).Value);
    }

    [Fact]
    public void FileOwnerService_ResetOwner_UsesDescriptorAccessorWrite()
    {
        var security = new FileSecurity();
        var accessor = new FakeDescriptorAccessor(TestPath, security);
        var pathInfo = new TestFileSystemPathInfo().AddFile(TestPath);
        var service = new FileOwnerService(Mock.Of<ILoggingService>(), pathInfo, accessor);

        service.ResetOwner(TestPath, recursive: false);

        Assert.Equal(1, accessor.SetOwnerAndAclCalls);
        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value;
        Assert.Equal(adminsSid, accessor.StoredOwnerSid);
        Assert.Equal(adminsSid, ((SecurityIdentifier)accessor.GetStoredSecurity().GetOwner(typeof(SecurityIdentifier))!).Value);
    }

    [Fact]
    public void AdminRestrictionAclWriter_RestrictToAdmins_UsesDescriptorAccessorMutation()
    {
        var accessor = new FakeDescriptorAccessor(TestPath, new FileSecurity());
        var writer = new AdminRestrictionAclWriter(accessor);

        writer.RestrictToAdmins(TestPath);

        Assert.Equal(1, accessor.ModifyAclCalls);
        var rules = accessor.GetStoredSecurity()
            .GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToList();
        Assert.All(rules, rule =>
            Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights));
        Assert.Contains(rules, rule => ((SecurityIdentifier)rule.IdentityReference).IsWellKnown(WellKnownSidType.LocalSystemSid));
        Assert.Contains(rules, rule => ((SecurityIdentifier)rule.IdentityReference).IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));
        var currentProcessSid = AdminOperationMockAccessHelper.GetCurrentProcessSidWhenUsingMocks();
        if (currentProcessSid == null)
        {
            Assert.Equal(2, rules.Count);
        }
        else
        {
            Assert.Equal(3, rules.Count);
            Assert.Contains(rules, rule => rule.IdentityReference.Equals(currentProcessSid));
        }
    }

    [Fact]
    public void TraverseAcl_RemoveTraverseOnlyAce_UsesNonPropagatingAccessorWrite()
    {
        using var tempDir = new TempDirectory("RunFence_TraverseAclRouting");
        var sid = new SecurityIdentifier(TestSid);
        var accessor = new FakeDescriptorAccessor(tempDir.Path, new DirectorySecurity());
        var traverseAcl = new TraverseAcl(accessor);
        traverseAcl.AddAllowAce(tempDir.Path, sid);
        accessor.GetStoredSecurity().AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            TraverseRightsHelper.TraverseRights,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        accessor.ResetCallCounts();

        traverseAcl.RemoveTraverseOnlyAce(tempDir.Path, sid);

        Assert.Equal(1, accessor.ApplyNonPropagatingAclCalls);
        var rules = accessor.GetStoredSecurity()
            .GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToList();
        Assert.DoesNotContain(rules, rule =>
            rule.AccessControlType == AccessControlType.Allow &&
            rule.IdentityReference.Equals(sid) &&
            rule.FileSystemRights == TraverseRightsHelper.TraverseRights &&
            rule.InheritanceFlags == InheritanceFlags.None);
        Assert.Contains(rules, rule =>
            rule.AccessControlType == AccessControlType.Allow &&
            ((SecurityIdentifier)rule.IdentityReference).IsWellKnown(WellKnownSidType.AuthenticatedUserSid));
    }

    [Fact]
    public void AclAccessor_SetOwnerWithFallback_RoutesThroughBackupPrivilegeOwnerOnlyPath()
    {
        using var tempDir = new TempDirectory("RunFence_AclAccessorSetOwnerPathRoute");
        var targetPath = Path.Combine(tempDir.Path, "target.txt");
        File.WriteAllText(targetPath, "payload");

        var native = new RoutingBackupPrivilegeSecurityNative(CreateFileSecurityDescriptorBytes());
        var aclAccessor = new AclAccessor(
            new AclAccessorNative(),
            new BackupPrivilegeSecurityDescriptorAccessor(native));
        var ownerSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        aclAccessor.SetOwnerWithFallback(targetPath, ownerSid);

        var createCall = Assert.Single(native.CreateFileCalls);
        Assert.Equal(targetPath, createCall.Path);
        Assert.Equal(
            FileSecurityNative.READ_CONTROL | FileSecurityNative.WRITE_OWNER,
            createCall.DesiredAccess);
        Assert.Equal(
            FileSecurityNative.FILE_SHARE_READ | FileSecurityNative.FILE_SHARE_WRITE,
            createCall.ShareMode);
        Assert.Equal(
            FileSecurityNative.OPEN_EXISTING,
            createCall.CreationDisposition);
        Assert.Equal(
            FileSecurityNative.FILE_FLAG_BACKUP_SEMANTICS,
            createCall.FlagsAndAttributes);
        var setSecurityInfoCall = Assert.Single(native.SetSecurityInfoCalls);
        Assert.Equal(new IntPtr(1234), setSecurityInfoCall.Handle);
        Assert.Equal(
            FileSecurityNative.SECURITY_INFORMATION.OWNER_SECURITY_INFORMATION,
            setSecurityInfoCall.SecurityInformation);
        Assert.NotEqual(IntPtr.Zero, setSecurityInfoCall.Owner);
        Assert.Equal(IntPtr.Zero, setSecurityInfoCall.Dacl);
        Assert.Equal(IntPtr.Zero, setSecurityInfoCall.Group);
        Assert.Equal(IntPtr.Zero, setSecurityInfoCall.Sacl);
        Assert.Equal([native.Handle], native.ClosedHandles);
    }

    private sealed class FakeDescriptorAccessor(string path, FileSystemSecurity security) : IPathSecurityDescriptorAccessor
    {
        private readonly string _path = path;
        private FileSystemSecurity _security = security;

        public int GetSecurityCalls { get; private set; }
        public int ModifyAclCalls { get; private set; }
        public int ModifyOwnerAndAclCalls { get; private set; }
        public int SetOwnerAndAclCalls { get; private set; }
        public int SetOwnerWithFallbackCalls { get; private set; }
        public int ApplyNonPropagatingAclCalls { get; private set; }
        public string? StoredOwnerSid { get; private set; }

        public FileSystemSecurity GetStoredSecurity() => _security;

        public void ResetCallCounts()
        {
            GetSecurityCalls = 0;
            ModifyAclCalls = 0;
            ModifyOwnerAndAclCalls = 0;
            SetOwnerAndAclCalls = 0;
            SetOwnerWithFallbackCalls = 0;
            ApplyNonPropagatingAclCalls = 0;
        }

        public FileSystemSecurity GetSecurity(string requestedPath)
        {
            Assert.Equal(_path, requestedPath);
            GetSecurityCalls++;
            return _security;
        }

        public string? GetOwnerSid(string requestedPath)
        {
            Assert.Equal(_path, requestedPath);
            return StoredOwnerSid;
        }

        public bool PathExists(string requestedPath, out bool isFolder)
        {
            Assert.Equal(_path, requestedPath);
            isFolder = true;
            return true;
        }

        public bool ModifyAclWithFallback(string requestedPath, Func<FileSystemSecurity, bool> modify)
        {
            Assert.Equal(_path, requestedPath);
            ModifyAclCalls++;
            return modify(_security);
        }

        public bool ModifyOwnerAndAclWithFallback(string requestedPath, Func<FileSystemSecurity, bool> modify)
        {
            Assert.Equal(_path, requestedPath);
            ModifyOwnerAndAclCalls++;
            return modify(_security);
        }

        public void SetOwnerAndAclWithFallback(string requestedPath, FileSystemSecurity securityToStore)
        {
            Assert.Equal(_path, requestedPath);
            SetOwnerAndAclCalls++;
            _security = securityToStore;
            StoredOwnerSid = (securityToStore.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier)?.Value;
        }

        public void SetOwnerWithFallback(string requestedPath, SecurityIdentifier ownerSid)
        {
            Assert.Equal(_path, requestedPath);
            SetOwnerWithFallbackCalls++;
            _security.SetOwner(ownerSid);
            StoredOwnerSid = ownerSid.Value;
        }

        public void ApplyNonPropagatingAcl(string requestedPath, FileSystemSecurity securityToStore)
        {
            Assert.Equal(_path, requestedPath);
            ApplyNonPropagatingAclCalls++;
            _security = securityToStore;
        }
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
