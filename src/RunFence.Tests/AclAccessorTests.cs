using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.InteropServices;
using RunFence.Acl;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class AclAccessorTests
{
    [Fact]
    public void PathExists_ExistingFileDirectoryAndMissingPath_ReportExpectedKind()
    {
        using var tempDir = new TempDirectory("RunFence_AclAccessorKinds");
        var filePath = Path.Combine(tempDir.Path, "file.txt");
        File.WriteAllText(filePath, "x");
        var missingPath = Path.Combine(tempDir.Path, "missing.txt");
        var accessor = AclAccessorFactory.Create();

        Assert.True(accessor.PathExists(tempDir.Path, out var dirIsFolder));
        Assert.True(dirIsFolder);
        Assert.True(accessor.PathExists(filePath, out var fileIsFolder));
        Assert.False(fileIsFolder);
        Assert.False(accessor.PathExists(missingPath, out var missingIsFolder));
        Assert.False(missingIsFolder);
    }

    [Fact]
    public void PathExists_AccessDeniedDirectory_StillReportsDirectory()
    {
        var userSid = WindowsIdentity.GetCurrent().User;
        Assert.NotNull(userSid);

        using var tempDir = new TempDirectory("RunFence_AclAccessorDenied");
        var deniedDir = Path.Combine(tempDir.Path, "denied");
        Directory.CreateDirectory(deniedDir);
        using var deniedAcl = TemporaryDenyAcl.Apply(
            deniedDir,
            userSid,
            FileSystemRights.ReadPermissions | FileSystemRights.ListDirectory | FileSystemRights.ReadAttributes);

        var accessor = AclAccessorFactory.Create();

        Assert.True(accessor.PathExists(deniedDir, out var isFolder));
        Assert.True(isFolder);
    }

    [Fact]
    public void ModifyAclWithFallback_ExistingDirectory_ProvidesDirectorySecurity()
    {
        using var tempDir = new TempDirectory("RunFence_AclAccessorDirModify");
        var accessor = AclAccessorFactory.Create();
        FileSystemSecurity? seenSecurity = null;

        accessor.ModifyAclWithFallback(tempDir.Path, security =>
        {
            seenSecurity = security;
            return false;
        });

        Assert.IsType<DirectorySecurity>(seenSecurity);
    }

    [Fact]
    public void ModifyAclWithFallback_ExistingFile_ProvidesFileSecurity()
    {
        using var tempDir = new TempDirectory("RunFence_AclAccessorFileModify");
        var filePath = Path.Combine(tempDir.Path, "file.txt");
        File.WriteAllText(filePath, "x");
        var accessor = AclAccessorFactory.Create();
        FileSystemSecurity? seenSecurity = null;

        accessor.ModifyAclWithFallback(filePath, security =>
        {
            seenSecurity = security;
            return false;
        });

        Assert.IsType<FileSecurity>(seenSecurity);
    }

    [Fact]
    public void ApplyNonPropagatingAcl_AcceptsDirectorySecurityPassedAsFileSystemSecurity()
    {
        using var tempDir = new TempDirectory("RunFence_AclAccessorNonProp");
        var accessor = AclAccessorFactory.Create();
        var sid = WindowsIdentity.GetCurrent().User;
        Assert.NotNull(sid);

        var security = (DirectorySecurity)accessor.GetSecurity(tempDir.Path);
        security.AddAccessRule(new FileSystemAccessRule(
            sid!,
            FileSystemRights.ReadAttributes,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));

        accessor.ApplyNonPropagatingAcl(tempDir.Path, security);

        var appliedRules = accessor.GetSecurity(tempDir.Path)
            .GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>();
        Assert.Contains(appliedRules, rule =>
            rule.IdentityReference is SecurityIdentifier ruleSid &&
            ruleSid.Equals(sid) &&
            rule.AccessControlType == AccessControlType.Allow &&
            (rule.FileSystemRights & FileSystemRights.ReadAttributes) == FileSystemRights.ReadAttributes);
    }

    [Fact]
    public void ApplyNonPropagatingAcl_AccessDenied_UsesNonPropagatingBackupFallback()
    {
        var setFileSecurityNative = new FakeAclAccessorNative
        {
            ErrorCode = 5
        };
        var backupNative = new FakeBackupPrivilegeSecurityNative();
        var accessor = new AclAccessor(
            setFileSecurityNative,
            new BackupPrivilegeSecurityDescriptorAccessor(backupNative));
        var security = new DirectorySecurity();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAttributes,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));

        accessor.ApplyNonPropagatingAcl(@"C:\protected", security);

        var createFileCall = Assert.Single(backupNative.CreateFileCalls);
        Assert.Equal(@"C:\protected", createFileCall.Path);
        Assert.Equal(FileSecurityNative.MAXIMUM_ALLOWED, createFileCall.DesiredAccess);
        Assert.Equal(
            FileSecurityNative.FILE_FLAG_BACKUP_SEMANTICS,
            createFileCall.FlagsAndAttributes);
        var setSecurityInfoCall = Assert.Single(backupNative.SetSecurityInfoCalls);
        Assert.Equal(
            FileSecurityNative.SECURITY_INFORMATION.DACL_SECURITY_INFORMATION |
            FileSecurityNative.SECURITY_INFORMATION.UNPROTECTED_DACL_SECURITY_INFORMATION,
            setSecurityInfoCall.SecurityInformation);
    }

    private sealed class FakeAclAccessorNative : IAclAccessorNative
    {
        public int ErrorCode { get; set; }

        public bool SetFileSecurity(string path, uint securityInformation, IntPtr securityDescriptor)
        {
            Marshal.SetLastPInvokeError(ErrorCode);
            return false;
        }
    }

    private sealed class FakeBackupPrivilegeSecurityNative : IBackupPrivilegeSecurityNative
    {
        private readonly byte[] securityDescriptorBytes = new DirectorySecurity().GetSecurityDescriptorBinaryForm();

        public IntPtr Handle { get; } = new(1234);
        public List<CreateFileCall> CreateFileCalls { get; } = [];
        public List<SetSecurityInfoCall> SetSecurityInfoCalls { get; } = [];

        public IntPtr CreateFile(
            string path,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile)
        {
            CreateFileCalls.Add(new CreateFileCall(path, desiredAccess, shareMode, creationDisposition, flagsAndAttributes));
            return Handle;
        }

        public void CloseHandle(IntPtr handle)
        {
        }

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
            securityDescriptor = Marshal.AllocHGlobal(securityDescriptorBytes.Length);
            Marshal.Copy(securityDescriptorBytes, 0, securityDescriptor, securityDescriptorBytes.Length);
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
            return 0;
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
            out bool ownerDefaulted) =>
            FileSecurityNative.GetSecurityDescriptorOwner(
                securityDescriptor,
                out owner,
                out ownerDefaulted);

        public void LocalFree(IntPtr securityDescriptor) =>
            Marshal.FreeHGlobal(securityDescriptor);
    }

    private sealed record CreateFileCall(
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
