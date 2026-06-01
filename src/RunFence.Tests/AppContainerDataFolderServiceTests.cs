using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Container;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class AppContainerDataFolderServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "RunFence_AppContainerAcl_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootPath))
                Directory.Delete(_rootPath, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void EnsureContainerDataFolder_HardensProgramDataRootAndAcRootBeforeCreatingProfileDirectories()
    {
        var aclAccessor = new FakeAclAccessor();
        var grantMutatorService = new Mock<IGrantMutatorService>(MockBehavior.Strict);
        var traverseService = new Mock<ITraverseService>(MockBehavior.Strict);
        var directoryProvisioningService = new Mock<IProgramDataDirectoryProvisioningService>(MockBehavior.Strict);
        var objectRepairService = new Mock<IProgramDataManagedObjectRepairService>(MockBehavior.Strict);
        var entry = new AppContainerEntry { Name = "ram_harden_root" };
        var containerSid = "S-1-15-2-1";
        var dataRoot = Path.Combine(_rootPath, entry.Name);
        var sequence = new MockSequence();
        void AssertProfileTreeNotCreatedYet()
        {
            Assert.False(Directory.Exists(dataRoot));
            Assert.False(Directory.Exists(Path.Combine(dataRoot, "Temp")));
            Assert.False(Directory.Exists(Path.Combine(dataRoot, "Roaming")));
            Assert.False(Directory.Exists(Path.Combine(dataRoot, "Local")));
            Assert.False(Directory.Exists(Path.Combine(dataRoot, "ProgramData")));
        }

        directoryProvisioningService.InSequence(sequence)
            .Setup(service => service.EnsureRoot())
            .Returns(() =>
            {
                AssertProfileTreeNotCreatedYet();
                return PathConstants.ProgramDataDir;
            });
        directoryProvisioningService.InSequence(sequence)
            .Setup(service => service.EnsureKnownDirectory(ProgramDataPolicies.Ac))
            .Returns(() =>
            {
                AssertProfileTreeNotCreatedYet();
                return _rootPath;
            });
        objectRepairService.InSequence(sequence)
            .Setup(service => service.EnsureManagedDirectoryOwner(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>()))
            .Returns(false);
        objectRepairService.InSequence(sequence)
            .Setup(service => service.EnsureManagedDirectoryOwner(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>()))
            .Returns(false);
        objectRepairService.InSequence(sequence)
            .Setup(service => service.EnsureManagedDirectoryOwner(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>()))
            .Returns(false);
        objectRepairService.InSequence(sequence)
            .Setup(service => service.EnsureManagedDirectoryOwner(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>()))
            .Returns(false);
        objectRepairService.InSequence(sequence)
            .Setup(service => service.EnsureManagedDirectoryOwner(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>()))
            .Returns(false);
        grantMutatorService.InSequence(sequence)
            .Setup(p => p.AddGrant(
                containerSid,
                dataRoot,
                false,
                It.IsAny<SavedRightsState?>(),
                null,
                null))
            .Returns<string, string, bool, SavedRightsState?, Func<bool>?, RunFence.Persistence.IGrantIntentStore?>((sid, path, _, rights, _, _) =>
            {
                aclAccessor.AddAllowRule(path, sid, GrantRightsMapper.MapAllowRights(rights!, isFolder: true));
                return new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true);
            });
        traverseService.InSequence(sequence)
            .Setup(p => p.AddTraverse(containerSid, dataRoot))
            .Returns(new GrantApplyResult(TraverseApplied: true, DatabaseModified: true, DurableSaveCompleted: true));

        var service = CreateService(
            grantMutatorService.Object,
            traverseService.Object,
            aclAccessor,
            directoryProvisioningService.Object,
            objectRepairService.Object);

        service.EnsureContainerDataFolder(entry, containerSid);

        Assert.True(Directory.Exists(Path.Combine(dataRoot, "Temp")));
        Assert.True(Directory.Exists(Path.Combine(dataRoot, "Roaming")));
        Assert.True(Directory.Exists(Path.Combine(dataRoot, "Local")));
        Assert.True(Directory.Exists(Path.Combine(dataRoot, "ProgramData")));
        directoryProvisioningService.VerifyAll();
        objectRepairService.VerifyAll();
        grantMutatorService.VerifyAll();
        traverseService.VerifyAll();
    }

    [Fact]
    public void EnsureContainerDataFolder_RepairsProfileTreeOwnersWithContainerAndInteractiveSids()
    {
        var aclAccessor = new FakeAclAccessor();
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var traverseService = new Mock<ITraverseService>();
        var directoryProvisioningService = new Mock<IProgramDataDirectoryProvisioningService>(MockBehavior.Strict);
        var objectRepairService = new Mock<IProgramDataManagedObjectRepairService>(MockBehavior.Strict);
        var ownerCalls = new List<(string Path, IReadOnlyCollection<string> Owners)>();
        var entry = new AppContainerEntry { Name = "ram_success" };
        var dataRoot = Path.Combine(_rootPath, entry.Name);
        var containerSid = "S-1-15-2-42";

        directoryProvisioningService
            .Setup(service => service.EnsureRoot())
            .Returns(PathConstants.ProgramDataDir);
        directoryProvisioningService
            .Setup(service => service.EnsureKnownDirectory(ProgramDataPolicies.Ac))
            .Returns(_rootPath);
        directoryProvisioningService
            .Setup(service => service.EnsureKnownDirectory(ProgramDataPolicies.Ac))
            .Returns(_rootPath);
        objectRepairService
            .Setup(service => service.EnsureManagedDirectoryOwner(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>()))
            .Callback<string, IReadOnlyCollection<string>>((path, owners) => ownerCalls.Add((path, owners)))
            .Returns(false);
        grantMutatorService
            .Setup(p => p.AddGrant(
                It.IsAny<string>(),
                It.IsAny<string>(),
                false,
                It.IsAny<SavedRightsState?>(),
                null,
                null))
            .Returns<string, string, bool, SavedRightsState?, Func<bool>?, RunFence.Persistence.IGrantIntentStore?>((sid, path, _, rights, _, _) =>
            {
                aclAccessor.AddAllowRule(path, sid, GrantRightsMapper.MapAllowRights(rights!, isFolder: true));
                return new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true);
            });
        traverseService
            .Setup(p => p.AddTraverse(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new GrantApplyResult(TraverseApplied: true, DatabaseModified: true, DurableSaveCompleted: true));

        var service = CreateService(
            grantMutatorService.Object,
            traverseService.Object,
            aclAccessor,
            directoryProvisioningService.Object,
            objectRepairService.Object);

        service.EnsureContainerDataFolder(entry, containerSid);

        Assert.True(Directory.Exists(Path.Combine(dataRoot, "Temp")));
        Assert.True(Directory.Exists(Path.Combine(dataRoot, "Roaming")));
        Assert.True(Directory.Exists(Path.Combine(dataRoot, "Local")));
        Assert.True(Directory.Exists(Path.Combine(dataRoot, "ProgramData")));
        Assert.Equal(
            new[]
            {
                dataRoot,
                Path.Combine(dataRoot, "Temp"),
                Path.Combine(dataRoot, "Roaming"),
                Path.Combine(dataRoot, "Local"),
                Path.Combine(dataRoot, "ProgramData")
            },
            ownerCalls.Select(call => call.Path).ToArray());
        Assert.All(ownerCalls, call => Assert.Contains(containerSid, call.Owners));
        var interactiveSid = NativeTokenHelper.TryGetInteractiveUserSid();
        if (interactiveSid != null)
        {
            Assert.All(ownerCalls, call => Assert.Contains(interactiveSid.Value, call.Owners));
        }

        traverseService.Verify(p => p.AddTraverse(containerSid, dataRoot), Times.Once);
        directoryProvisioningService.VerifyAll();
        objectRepairService.VerifyAll();
    }

    [Fact]
    public void EnsureDataFolderTraverse_WhenGrantIsNotVerifiable_Throws()
    {
        var aclAccessor = new FakeAclAccessor();
        var grantMutatorService = new Mock<IGrantMutatorService>();
        var traverseService = new Mock<ITraverseService>();
        grantMutatorService
            .Setup(p => p.AddGrant(
                It.IsAny<string>(),
                It.IsAny<string>(),
                false,
                It.IsAny<SavedRightsState?>(),
                null,
                null))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true));
        traverseService
            .Setup(p => p.AddTraverse(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new GrantApplyResult(TraverseApplied: true, DatabaseModified: true, DurableSaveCompleted: true));

        var service = CreateService(
            grantMutatorService.Object,
            traverseService.Object,
            aclAccessor,
            Mock.Of<IProgramDataDirectoryProvisioningService>(),
            Mock.Of<IProgramDataManagedObjectRepairService>());
        var entry = new AppContainerEntry { Name = "ram_missing_grant" };
        Directory.CreateDirectory(Path.Combine(_rootPath, entry.Name));

        Assert.Throws<InvalidOperationException>(() => service.EnsureDataFolderTraverse(entry, "S-1-15-2-77"));
    }

    private AppContainerDataFolderService CreateService(
        IGrantMutatorService grantMutatorService,
        ITraverseService traverseService,
        FakeAclAccessor aclAccessor,
        IProgramDataDirectoryProvisioningService programDataDirectoryProvisioningService,
        IProgramDataManagedObjectRepairService programDataManagedObjectRepairService)
        => new(
            grantMutatorService,
            traverseService,
            aclAccessor,
            programDataDirectoryProvisioningService,
            programDataManagedObjectRepairService,
            AppContainerProviderTestDoubles.CreatePathProvider(_rootPath));

    private sealed class FakeAclAccessor(IEnumerable<string>? ignoreModifyPaths = null) : IPathSecurityDescriptorAccessor
    {
        private readonly Dictionary<string, DirectorySecurity> _securities = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ignoreModifyPaths = new(ignoreModifyPaths ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        private const string HandleKey = "<handle>";

        public FileSystemSecurity GetSecurity(string path)
            => GetOrCreate(path);

        public FileSystemSecurity GetSecurity(SafeFileHandle handle, bool isDirectory)
            => GetOrCreate(HandleKey);

        public void ApplyExplicitAce(string path, string sid, AccessControlType type, FileSystemRights rights, Func<FileSystemAccessRule, bool>? shouldSkip = null)
            => throw new NotSupportedException();

        public void RemoveExplicitAces(string path, string sid, AccessControlType type, Func<FileSystemAccessRule, bool>? shouldSkip = null)
            => throw new NotSupportedException();

        public bool PathExists(string path, out bool isFolder)
        {
            isFolder = Directory.Exists(path);
            return isFolder || File.Exists(path);
        }

        public bool ModifyAclWithFallback(string path, Func<FileSystemSecurity, bool> modify)
        {
            var security = GetOrCreate(path);
            if (_ignoreModifyPaths.Contains(path))
                return false;

            return modify(security);
        }

        public bool ModifyAclWithFallback(SafeFileHandle handle, bool isFolder, Func<FileSystemSecurity, bool> modify)
        {
            var security = GetOrCreate(HandleKey);
            if (_ignoreModifyPaths.Contains(HandleKey))
                return false;

            return modify(security);
        }

        public bool ModifyOwnerAndAclWithFallback(string path, Func<FileSystemSecurity, bool> modify)
            => ModifyAclWithFallback(path, modify);

        public void SetOwnerAndAclWithFallback(string path, FileSystemSecurity security)
            => _securities[path] = (DirectorySecurity)security;

        public void SetOwnerWithFallback(string path, SecurityIdentifier ownerSid)
            => GetOrCreate(path).SetOwner(ownerSid);

        public void SetOwnerWithFallback(SafeFileHandle handle, SecurityIdentifier ownerSid)
            => GetOrCreate(HandleKey).SetOwner(ownerSid);

        public string? GetOwnerSid(string path)
            => null;

        public void ApplyNonPropagatingAcl(string path, FileSystemSecurity security)
            => _securities[path] = (DirectorySecurity)security;

        public void AddAllowRule(string path, string sid, FileSystemRights rights)
        {
            var security = GetOrCreate(path);
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid),
                rights,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        private DirectorySecurity GetOrCreate(string path)
        {
            if (!_securities.TryGetValue(path, out var security))
            {
                security = new DirectorySecurity();
                _securities[path] = security;
            }

            return security;
        }
    }
}
