using System.ComponentModel;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Moq;
using RunFence.Acl;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class ProgramDataSecurityCoreTests
{
    [Fact]
    public void ProgramDataPathGuard_NormalizeRelativePath_RejectsParentTraversal()
    {
        var guard = new ProgramDataPathGuard();

        Assert.Throws<InvalidOperationException>(() => guard.NormalizeRelativePath(@"icons\..\..\evil"));
    }

    [Fact]
    public void ProgramDataPathGuard_NormalizeRelativePath_RejectsAbsolutePath()
    {
        var guard = new ProgramDataPathGuard();

        Assert.Throws<InvalidOperationException>(() => guard.NormalizeRelativePath(@"C:\Temp\evil"));
    }

    [Fact]
    public void ProgramDataSecurityTestScope_FakeProgramDataPathGuard_NormalizeRelativePath_RejectsInvalidValues()
    {
        using var scope = new ProgramDataSecurityTestScope();
        var guard = scope.PathGuard;

        Assert.Throws<InvalidOperationException>(() => guard.NormalizeRelativePath(null!));
        Assert.Throws<InvalidOperationException>(() => guard.NormalizeRelativePath(string.Empty));
        Assert.Throws<InvalidOperationException>(() => guard.NormalizeRelativePath(" "));
        Assert.Throws<InvalidOperationException>(() => guard.NormalizeRelativePath("."));
        Assert.Throws<InvalidOperationException>(() => guard.NormalizeRelativePath(".."));
        Assert.Throws<InvalidOperationException>(() => guard.NormalizeRelativePath(@"icons\..\badge.ico"));
        Assert.Throws<InvalidOperationException>(() => guard.NormalizeRelativePath(@"icons\.\badge.ico"));
        Assert.Throws<InvalidOperationException>(() => guard.NormalizeRelativePath(@"icons\\badge.ico"));
    }

    [Fact]
    public void ProgramDataSecurityTestScope_FakeProgramDataPathGuard_NormalizeRelativePath_AcceptsSafeRelativePath()
    {
        using var scope = new ProgramDataSecurityTestScope();
        var guard = scope.PathGuard;

        var normalized = guard.NormalizeRelativePath(Path.Combine("icons", "badge.ico"));

        Assert.Equal(Path.Combine(scope.RootPath, "icons", "badge.ico"), normalized);
    }

    [Fact]
    public void ProgramDataPathGuard_IsUnderRoot_ReturnsFalseForRelativePath()
    {
        var guard = new ProgramDataPathGuard();

        Assert.False(guard.IsUnderRoot(@"icons\badge.ico"));
    }

    [Fact]
    public void ProgramDataPathGuard_NormalizeAbsolutePathUnderRoot_AcceptsRoot()
    {
        var guard = new ProgramDataPathGuard();

        Assert.Equal(guard.NormalizeRoot(), guard.NormalizeAbsolutePathUnderRoot(guard.NormalizeRoot()));
    }

    [Fact]
    public void ProgramDataPathGuard_NormalizeAbsolutePathUnderRoot_TrimsTrailingSeparators()
    {
        var guard = new ProgramDataPathGuard();
        var childPath = Path.Combine(guard.NormalizeRoot(), "icons");

        var normalized = guard.NormalizeAbsolutePathUnderRoot(childPath + Path.DirectorySeparatorChar);

        Assert.Equal(childPath, normalized);
    }

    [Fact]
    public void ProgramDataPathGuard_NormalizeAbsolutePathUnderRoot_NormalizesDotSegments()
    {
        var guard = new ProgramDataPathGuard();
        var expectedPath = Path.Combine(guard.NormalizeRoot(), "icons", "badge.ico");
        var pathWithDotSegment = Path.Combine(guard.NormalizeRoot(), "icons", ".", "badge.ico");

        var normalized = guard.NormalizeAbsolutePathUnderRoot(pathWithDotSegment);

        Assert.Equal(expectedPath, normalized);
    }

    [Fact]
    public void ProgramDataPathGuard_NormalizeAbsolutePathUnderRoot_RejectsOutsideRoot()
    {
        var guard = new ProgramDataPathGuard();
        var outsideRoot = guard.NormalizeRoot() + "-outside";

        Assert.Throws<InvalidOperationException>(() =>
            guard.NormalizeAbsolutePathUnderRoot(Path.Combine(outsideRoot, "badge.ico")));
    }

    [Fact]
    public void ProgramDataOwnerRepairService_RepairOwner_PreservesCurrentOwner()
    {
        using var currentIdentity = WindowsIdentity.GetCurrent();
        var currentSid = currentIdentity.User ?? throw new InvalidOperationException("Current test account SID was not available.");
        var accessor = new FakeDescriptorAccessor(currentSid);
        var policyCatalog = new ProgramDataPathPolicyCatalog(new FakeProgramDataPathGuard());
        var service = new ProgramDataOwnerRepairService(
            Mock.Of<ILoggingService>(),
            accessor,
            new ProgramDataOwnerPolicyService(policyCatalog));

        using var handle = new SafeFileHandle(new IntPtr(1001), ownsHandle: false);
        service.RepairOwner(handle, @"C:\ProgramData\RunFence\safe", isDirectory: true, ProgramDataAllowedOwnerPolicy.AdministratorsOrCurrentAccount);

        Assert.Equal(0, accessor.SetOwnerWithHandleCalls);
    }

    [Fact]
    public void ProgramDataOwnerRepairService_RepairOwner_ReplacesDisallowedOwner()
    {
        using var currentIdentity = WindowsIdentity.GetCurrent();
        var currentSid = currentIdentity.User ?? throw new InvalidOperationException("Current test account SID was not available.");
        var previousOwnerSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var accessor = new FakeDescriptorAccessor(previousOwnerSid);
        var policyCatalog = new ProgramDataPathPolicyCatalog(new FakeProgramDataPathGuard());
        var service = new ProgramDataOwnerRepairService(
            Mock.Of<ILoggingService>(),
            accessor,
            new ProgramDataOwnerPolicyService(policyCatalog));

        using var handle = new SafeFileHandle(new IntPtr(1002), ownsHandle: false);
        service.RepairOwner(handle, @"C:\ProgramData\RunFence\unsafe.txt", isDirectory: false, ProgramDataAllowedOwnerPolicy.AdministratorsOrCurrentAccount);

        Assert.Equal(1, accessor.SetOwnerWithHandleCalls);
        Assert.Equal(currentSid.Value, accessor.CurrentOwner.Value);
    }

    [Fact]
    public void OpenExistingManagedObject_AcceptsExistingManagedFile()
    {
        using var scope = new RealProgramDataTestScope();
        var guard = new ProgramDataPathGuard();
        var filePath = scope.CreateFile("badge.ico");

        using var handle = guard.OpenExistingManagedObject(
            filePath,
            ProgramDataObjectKind.File,
            ProgramDataManagedObjectAccess.Validate);

        Assert.False(handle.IsInvalid);
    }

    [Fact]
    public void OpenExistingManagedObject_AcceptsExistingManagedDirectory()
    {
        using var scope = new RealProgramDataTestScope();
        var guard = new ProgramDataPathGuard();
        var directoryPath = scope.CreateDirectory("Temp");

        using var handle = guard.OpenExistingManagedObject(
            directoryPath,
            ProgramDataObjectKind.Directory,
            ProgramDataManagedObjectAccess.Validate);

        Assert.False(handle.IsInvalid);
    }

    [Fact]
    public void OpenExistingManagedObject_FilePathPassedAsDirectoryKind_ThrowsInvalidOperation()
    {
        using var scope = new RealProgramDataTestScope();
        var guard = new ProgramDataPathGuard();
        var filePath = scope.CreateFile("badge.ico");

        Assert.Throws<InvalidOperationException>(() => guard.OpenExistingManagedObject(
            filePath,
            ProgramDataObjectKind.Directory,
            ProgramDataManagedObjectAccess.Validate));
    }

    [Fact]
    public void OpenExistingManagedObject_DirectoryPathPassedAsFileKind_ThrowsInvalidOperation()
    {
        using var scope = new RealProgramDataTestScope();
        var guard = new ProgramDataPathGuard();
        var directoryPath = scope.CreateDirectory("Temp");

        Assert.Throws<InvalidOperationException>(() => guard.OpenExistingManagedObject(
            directoryPath,
            ProgramDataObjectKind.File,
            ProgramDataManagedObjectAccess.Validate));
    }

    [Fact]
    public void OpenExistingManagedObject_MissingPath_ThrowsWin32Exception()
    {
        using var scope = new RealProgramDataTestScope();
        var guard = new ProgramDataPathGuard();
        var missingPath = Path.Combine(scope.RootPath, "missing.ico");

        Assert.Throws<Win32Exception>(() => guard.OpenExistingManagedObject(
            missingPath,
            ProgramDataObjectKind.File,
            ProgramDataManagedObjectAccess.Validate));
    }

    [Fact]
    public void OpenExistingManagedObject_ReparsePointDirectory_ThrowsInvalidOperation()
    {
        using var scope = new RealProgramDataTestScope();
        var guard = new ProgramDataPathGuard();
        var targetPath = scope.CreateDirectory("junction-target");
        var junctionPath = scope.CreateDirectory("reparse");
        JunctionHelper.CreateJunction(junctionPath, targetPath);

        Assert.Throws<InvalidOperationException>(() => guard.OpenExistingManagedObject(
            junctionPath,
            ProgramDataObjectKind.Directory,
            ProgramDataManagedObjectAccess.Validate));
    }

    private sealed class FakeDescriptorAccessor(SecurityIdentifier ownerSid) : IHandleSecurityDescriptorAccessor
    {
        public SecurityIdentifier CurrentOwner { get; private set; } = ownerSid;
        public int SetOwnerWithHandleCalls { get; private set; }

        public FileSystemSecurity GetSecurity(SafeFileHandle handle, bool isDirectory)
        {
            FileSystemSecurity security = isDirectory ? new DirectorySecurity() : new FileSecurity();
            security.SetOwner(CurrentOwner);
            return security;
        }

        public bool ModifyAclWithFallback(SafeFileHandle handle, bool isFolder, Func<FileSystemSecurity, bool> modify)
            => throw new NotSupportedException();

        public void SetOwnerWithFallback(SafeFileHandle handle, SecurityIdentifier ownerSid)
        {
            SetOwnerWithHandleCalls++;
            CurrentOwner = ownerSid;
        }
    }

    private sealed class FakeProgramDataPathGuard : IProgramDataPathGuard
    {
        public string NormalizeRoot() => @"C:\ProgramData\RunFence";

        public string NormalizeRelativePath(string relativePath) => Path.Combine(NormalizeRoot(), relativePath);

        public string NormalizeAbsolutePathUnderRoot(string path) => path;

        public string NormalizeExistingPathUnderRoot(string path, ProgramDataObjectKind kind)
            => NormalizeAbsolutePathUnderRoot(path);

        public SafeFileHandle OpenExistingManagedObject(
            string path,
            ProgramDataObjectKind kind,
            ProgramDataManagedObjectAccess access)
        {
            return new SafeFileHandle(new IntPtr(2001), ownsHandle: false);
        }

        public bool IsUnderRoot(string path) => true;
    }

    private sealed class RealProgramDataTestScope : IDisposable
    {
        private readonly string _managedRoot;

        public RealProgramDataTestScope()
        {
            _managedRoot = Path.GetFullPath(
                Path.Combine(PathConstants.ProgramDataDir, $"RunFence_ProgramDataSecurityTests_{Guid.NewGuid():N}"));
            Directory.CreateDirectory(PathConstants.ProgramDataDir);
            Directory.CreateDirectory(_managedRoot);
        }

        public string RootPath => _managedRoot;

        public string CreateDirectory(string relativePath)
        {
            var path = Path.Combine(_managedRoot, relativePath);
            Directory.CreateDirectory(path);
            return path;
        }

        public string CreateFile(string relativePath)
        {
            var path = Path.Combine(_managedRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var file = File.Create(path))
            {
                file.WriteByte(1);
            }
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_managedRoot))
                    Directory.Delete(_managedRoot, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
