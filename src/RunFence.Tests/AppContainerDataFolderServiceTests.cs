using System.Security.AccessControl;
using System.Security.Principal;
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
        catch
        {
        }
    }

    [Fact]
    public void EnsureContainerDataFolder_WhenRootAclCannotBeVerified_Throws()
    {
        var aclAccessor = new FakeAclAccessor(ignoreModifyPaths: [_rootPath]);
        var pathGrantService = new Mock<IPathGrantService>();
        var service = CreateService(pathGrantService.Object, aclAccessor);

        var entry = new AppContainerEntry { Name = "ram_fail_root" };

        Assert.Throws<InvalidOperationException>(() => service.EnsureContainerDataFolder(entry, "S-1-15-2-1"));
        pathGrantService.Verify(p => p.AddGrant(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<SavedRightsState?>(),
            It.IsAny<Func<bool>?>(),
            It.IsAny<RunFence.Persistence.IGrantIntentStore?>()), Times.Never);
    }

    [Fact]
    public void EnsureContainerDataFolder_WhenGrantAppliedAndVerified_CreatesSubfoldersAndTraverse()
    {
        var aclAccessor = new FakeAclAccessor();
        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
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
        pathGrantService
            .Setup(p => p.AddTraverse(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new GrantApplyResult(TraverseApplied: true, DatabaseModified: true, DurableSaveCompleted: true));

        var service = CreateService(pathGrantService.Object, aclAccessor);
        var entry = new AppContainerEntry { Name = "ram_success" };

        service.EnsureContainerDataFolder(entry, "S-1-15-2-42");

        var dataRoot = Path.Combine(_rootPath, entry.Name);
        Assert.True(Directory.Exists(Path.Combine(dataRoot, "Temp")));
        Assert.True(Directory.Exists(Path.Combine(dataRoot, "Roaming")));
        Assert.True(Directory.Exists(Path.Combine(dataRoot, "Local")));
        Assert.True(Directory.Exists(Path.Combine(dataRoot, "ProgramData")));
        pathGrantService.Verify(p => p.AddTraverse("S-1-15-2-42", dataRoot), Times.Once);
    }

    [Fact]
    public void EnsureDataFolderTraverse_WhenGrantIsNotVerifiable_Throws()
    {
        var aclAccessor = new FakeAclAccessor();
        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
            .Setup(p => p.AddGrant(
                It.IsAny<string>(),
                It.IsAny<string>(),
                false,
                It.IsAny<SavedRightsState?>(),
                null,
                null))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true));
        pathGrantService
            .Setup(p => p.AddTraverse(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new GrantApplyResult(TraverseApplied: true, DatabaseModified: true, DurableSaveCompleted: true));

        var service = CreateService(pathGrantService.Object, aclAccessor);
        var entry = new AppContainerEntry { Name = "ram_missing_grant" };
        Directory.CreateDirectory(Path.Combine(_rootPath, entry.Name));

        Assert.Throws<InvalidOperationException>(() => service.EnsureDataFolderTraverse(entry, "S-1-15-2-77"));
    }

    private AppContainerDataFolderService CreateService(IPathGrantService pathGrantService, FakeAclAccessor aclAccessor)
        => new(
            pathGrantService,
            aclAccessor,
            AppContainerProviderTestDoubles.CreatePathProvider(_rootPath));

    private sealed class FakeAclAccessor(IEnumerable<string>? ignoreModifyPaths = null) : IAclAccessor
    {
        private readonly Dictionary<string, DirectorySecurity> _securities = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ignoreModifyPaths = new(ignoreModifyPaths ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        public FileSystemSecurity GetSecurity(string path)
            => GetOrCreate(path);

        public void ApplyExplicitAce(string path, string sid, AccessControlType type, FileSystemRights rights, Func<FileSystemAccessRule, bool>? shouldSkip = null)
            => throw new NotSupportedException();

        public void RemoveExplicitAces(string path, string sid, AccessControlType type, Func<FileSystemAccessRule, bool>? shouldSkip = null)
            => throw new NotSupportedException();

        public bool PathExists(string path, out bool isFolder)
        {
            isFolder = Directory.Exists(path);
            return isFolder || File.Exists(path);
        }

        public bool ModifyAclWithFallback(string path, bool isFolder, Func<FileSystemSecurity, bool> modify)
        {
            var security = GetOrCreate(path);
            if (_ignoreModifyPaths.Contains(path))
                return false;

            return modify(security);
        }

        public bool ModifyOwnerAndAclWithFallback(string path, bool isFolder, Func<FileSystemSecurity, bool> modify)
            => ModifyAclWithFallback(path, isFolder, modify);

        public void SetOwnerAndAclWithFallback(string path, bool isFolder, FileSystemSecurity security)
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
