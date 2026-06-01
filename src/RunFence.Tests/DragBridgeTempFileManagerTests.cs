using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.DragBridge;
using Xunit;

namespace RunFence.Tests;

public class DragBridgeTempFileManagerTests : IDisposable
{
    private readonly Mock<ILoggingService> _log;
    private readonly Mock<ITraverseService> _traverseService;
    private readonly Mock<ITempDirectoryAclHelper> _aclHelper;
    private readonly ProgramDataMocks _programData;
    private readonly string _testBase;
    private readonly DragBridgeTempFileManager _manager;

    public DragBridgeTempFileManagerTests()
    {
        _log = new Mock<ILoggingService>();
        _traverseService = new Mock<ITraverseService>();
        _aclHelper = new Mock<ITempDirectoryAclHelper>();
        _aclHelper
            .Setup(s => s.ApplyRestrictedAcl(
                It.IsAny<DirectoryInfo>(),
                It.IsAny<(IdentityReference identity, FileSystemRights rights)[]>()))
            .Returns(new DirectorySecurity());
        _programData = CreateProgramDataMocks(isUnderRoot: false);
        _testBase = Path.Combine(Path.GetTempPath(), $"ram_test_{Guid.NewGuid():N}");
        _manager = CreateManager(_testBase, _programData);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_testBase, true);
        }
        catch
        {
        }
    }

    [Fact]
    public void CreateTempFolder_CreatesDirectory()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();

        var result = _manager.CreateTempFolder(currentSid);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.TempFolderPath);
        Assert.True(Directory.Exists(result.TempFolderPath));
        Assert.StartsWith(_testBase, result.TempFolderPath);
    }

    [Fact]
    public void CopyFilesToTemp_CopiesFiles()
    {
        using var tempSrc = new TempDirectory();
        var srcFile = Path.Combine(tempSrc.Path, "test.txt");
        File.WriteAllText(srcFile, "hello");

        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var destFolder = _manager.CreateTempFolder(currentSid).TempFolderPath!;

        var result = _manager.CopyFilesToTemp(destFolder, [srcFile]);

        var expectedDest = Path.Combine(destFolder, "test.txt");
        Assert.True(File.Exists(expectedDest));
        Assert.Equal("hello", File.ReadAllText(expectedDest));
        Assert.Single(result.TempPaths);
        Assert.Equal(expectedDest, result.TempPaths[0]);
    }

    [Fact]
    public void CopyFilesToTemp_WhenTempFolderIsManagedProgramData_HardensBeforeCopy()
    {
        using var tempSrc = new TempDirectory();
        var srcFile = Path.Combine(tempSrc.Path, "test.txt");
        File.WriteAllText(srcFile, "hello");
        var tempFolder = Path.Combine(_testBase, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        var programData = CreateProgramDataMocks(isUnderRoot: true);
        programData.DirectoryProvisioning
            .Setup(s => s.EnsureKnownDirectory(ProgramDataPolicies.DragBridge))
            .Returns(_testBase);
        programData.ObjectRepair
            .Setup(s => s.EnsureManagedDirectoryOwner(tempFolder))
            .Returns(false);
        var manager = CreateManager(_testBase, programData);

        var result = manager.CopyFilesToTemp(tempFolder, [srcFile]);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(tempFolder, "test.txt")));
        programData.DirectoryProvisioning.Verify(
            s => s.EnsureKnownDirectory(ProgramDataPolicies.DragBridge),
            Times.Once);
        programData.ObjectRepair.Verify(s => s.EnsureManagedDirectoryOwner(tempFolder), Times.Once);
    }

    [Fact]
    public void CopyFilesToTemp_WhenManagedTempFolderSecurityFails_ReturnsFailedEntries()
    {
        using var tempSrc = new TempDirectory();
        var srcFile = Path.Combine(tempSrc.Path, "test.txt");
        File.WriteAllText(srcFile, "hello");
        var tempFolder = Path.Combine(_testBase, Guid.NewGuid().ToString("N"));
        var programData = CreateProgramDataMocks(isUnderRoot: true);
        programData.DirectoryProvisioning
            .Setup(s => s.EnsureKnownDirectory(ProgramDataPolicies.DragBridge))
            .Returns(_testBase);
        programData.ObjectRepair
            .Setup(s => s.EnsureManagedDirectoryOwner(tempFolder))
            .Throws(new IOException("security failed"));
        var manager = CreateManager(_testBase, programData);

        var result = manager.CopyFilesToTemp(tempFolder, [srcFile]);

        Assert.False(result.Succeeded);
        Assert.Empty(result.TempPaths);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(srcFile, entry.SourcePath);
        Assert.Equal(DragBridgeTempFileCopyStatus.Failed, entry.CopyStatus);
        Assert.Contains("security failed", entry.ErrorText);
    }

    [Fact]
    public void CopyFilesToTemp_EmptyInput_ReturnsSuccessWithoutManagedPreflight()
    {
        var tempFolder = Path.Combine(_testBase, Guid.NewGuid().ToString("N"));
        var programData = CreateProgramDataMocks(isUnderRoot: true);
        var manager = CreateManager(_testBase, programData);

        var result = manager.CopyFilesToTemp(tempFolder, []);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Entries);
        Assert.Empty(result.TempPaths);
        programData.DirectoryProvisioning.Verify(
            s => s.EnsureKnownDirectory(It.IsAny<ProgramDataDirectoryPolicy>()),
            Times.Never);
    }

    [Fact]
    public void CopyFilesToTemp_HandlesNameCollision_ReturnsActualPath()
    {
        using var tempSrc = new TempDirectory();
        var file2 = Path.Combine(tempSrc.Path, "sub", "file.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file2)!);
        File.WriteAllText(file2, "second");

        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var destFolder = _manager.CreateTempFolder(currentSid).TempFolderPath!;

        // Pre-create a conflicting file so the copy must rename
        File.WriteAllText(Path.Combine(destFolder, "file.txt"), "existing");

        var returned = _manager.CopyFilesToTemp(destFolder, [file2]);

        // Should have returned the collision-renamed path, not the original name
        Assert.Single(returned.TempPaths);
        Assert.Equal(Path.Combine(destFolder, "file_1.txt"), returned.TempPaths[0]);
        Assert.True(File.Exists(returned.TempPaths[0]));
        // Original file unchanged
        Assert.Equal("existing", File.ReadAllText(Path.Combine(destFolder, "file.txt")));
    }

    [Fact]
    public void CopyFilesToTemp_NonExistentSource_LogsWarning()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var destFolder = _manager.CreateTempFolder(currentSid).TempFolderPath!;

        var result = _manager.CopyFilesToTemp(destFolder, [@"C:\nonexistent_file_xyz.txt"]);

        Assert.Empty(result.TempPaths);
        Assert.False(Directory.Exists(destFolder));
    }

    [Fact]
    public void CleanupOldFolders_DeletesFoldersOlderThanThreshold()
    {
        Directory.CreateDirectory(_testBase);
        var oldFolder = Path.Combine(_testBase, "old_folder");
        Directory.CreateDirectory(oldFolder);

        // Make the folder appear old by setting creation time
        Directory.SetCreationTimeUtc(oldFolder, DateTime.UtcNow.AddHours(-2));

        _manager.CleanupOldFolders(TimeSpan.FromHours(1));

        Assert.False(Directory.Exists(oldFolder));
    }

    [Fact]
    public void CleanupOldFolders_PreservesRecentFolders()
    {
        Directory.CreateDirectory(_testBase);
        var recentFolder = Path.Combine(_testBase, "recent_folder");
        Directory.CreateDirectory(recentFolder);

        _manager.CleanupOldFolders(TimeSpan.FromHours(1));

        Assert.True(Directory.Exists(recentFolder));
    }

    [Fact]
    public void CleanupOldFolders_NonExistentRoot_DoesNotThrow()
    {
        var pathResolver = new Mock<IProgramDataKnownPathResolver>();
        pathResolver.Setup(r => r.GetDirectoryPath(ProgramDataPolicies.DragBridge))
            .Returns(@"C:\nonexistent_base_xyz");
        var manager = new DragBridgeTempFileManager(
            _log.Object,
            _traverseService.Object,
            CreateAclHelper().Object,
            Mock.Of<IProgramDataDirectoryProvisioningService>(),
            Mock.Of<IProgramDataManagedObjectRepairService>(),
            Mock.Of<IProgramDataPathPolicyService>(),
            Mock.Of<IProgramDataObjectProvisioner>(),
            pathResolver.Object);

        var ex = Record.Exception(() => manager.CleanupOldFolders(TimeSpan.FromHours(1)));

        Assert.Null(ex);
    }

    [Fact]
    public void CreateTempFolder_EnsuresTraverseForTargetSid()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        _traverseService.Setup(s => s.AddTraverse(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new GrantApplyResult(TraverseApplied: true, DatabaseModified: true, DurableSaveCompleted: true));

        var result = _manager.CreateTempFolder(currentSid);

        Assert.True(result.Succeeded);
        _traverseService.Verify(s => s.AddTraverse(currentSid, _testBase), Times.Once);
    }

    [Fact]
    public void CreateTempFolder_WithContainerSid_EnsuresTraverseForBothSids()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        const string containerSid = "S-1-15-2-42";
        _traverseService.Setup(s => s.AddTraverse(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new GrantApplyResult(TraverseApplied: true, DatabaseModified: true, DurableSaveCompleted: true));

        var result = _manager.CreateTempFolder(currentSid, containerSid);

        Assert.True(result.Succeeded);
        _traverseService.Verify(s => s.AddTraverse(currentSid, _testBase), Times.Once);
        _traverseService.Verify(s => s.AddTraverse(containerSid, _testBase), Times.Once);
    }

    [Fact]
    public void CreateTempFolder_TraverseGrantFailure_ReturnsFailureResult()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        _traverseService.Setup(s => s.AddTraverse(It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new IOException("save failed"));

        var result = _manager.CreateTempFolder(currentSid);

        Assert.False(result.Succeeded);
        Assert.Null(result.TempFolderPath);
        Assert.Contains("save failed", result.ErrorMessage);
    }

    [Fact]
    public void CreateTempFolder_WhenTempRootIsManagedProgramData_HardensDragBridgeRootBeforeTraverse()
    {
        var programData = CreateProgramDataMocks(isUnderRoot: true);
        programData.DirectoryProvisioning
            .Setup(s => s.EnsureKnownDirectory(ProgramDataPolicies.DragBridge))
            .Returns(_testBase);
        _traverseService.Setup(s => s.AddTraverse(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new GrantApplyResult(TraverseApplied: true, DatabaseModified: true, DurableSaveCompleted: true));
        var manager = CreateManager(_testBase, programData);
        var currentSid = SidResolutionHelper.GetCurrentUserSid();

        var result = manager.CreateTempFolder(currentSid);

        Assert.True(result.Succeeded);
        programData.DirectoryProvisioning.Verify(
            s => s.EnsureKnownDirectory(ProgramDataPolicies.DragBridge),
            Times.Once);
        programData.ObjectProvisioner.Verify(
            s => s.CreateOrRepairDirectory(It.Is<ProgramDataExplicitDirectoryRequest>(request =>
                request.Path.StartsWith(_testBase, StringComparison.OrdinalIgnoreCase) &&
                request.Profile == ProgramDataDirectoryAclProfile.CurrentProcessUserFullControl &&
                request.ReplaceExistingSecurity &&
                request.AdditionalAccess.Count == 1)),
            Times.Once);
        _traverseService.Verify(s => s.AddTraverse(currentSid, _testBase), Times.Once);
    }

    private DragBridgeTempFileManager CreateManager(string tempRoot, ProgramDataMocks programData)
    {
        programData.PathResolver
            .Setup(s => s.GetDirectoryPath(ProgramDataPolicies.DragBridge))
            .Returns(tempRoot);
        return new(
            _log.Object,
            _traverseService.Object,
            _aclHelper.Object,
            programData.DirectoryProvisioning.Object,
            programData.ObjectRepair.Object,
            programData.PathPolicy.Object,
            programData.ObjectProvisioner.Object,
            programData.PathResolver.Object);
    }

    private static ProgramDataMocks CreateProgramDataMocks(bool isUnderRoot)
    {
        var mocks = new ProgramDataMocks();
        mocks.PathPolicy.Setup(s => s.IsUnderRoot(It.IsAny<string>())).Returns(isUnderRoot);
        mocks.ObjectProvisioner
            .Setup(s => s.CreateOrRepairDirectory(It.IsAny<ProgramDataExplicitDirectoryRequest>()))
            .Callback<ProgramDataExplicitDirectoryRequest>(request => Directory.CreateDirectory(request.Path));
        return mocks;
    }

    private static Mock<ITempDirectoryAclHelper> CreateAclHelper()
    {
        var helper = new Mock<ITempDirectoryAclHelper>();
        helper.Setup(s => s.ApplyRestrictedAcl(
                It.IsAny<DirectoryInfo>(),
                It.IsAny<(IdentityReference identity, FileSystemRights rights)[]>()))
            .Returns(new DirectorySecurity());
        return helper;
    }

    private sealed class ProgramDataMocks
    {
        public Mock<IProgramDataDirectoryProvisioningService> DirectoryProvisioning { get; } = new();
        public Mock<IProgramDataManagedObjectRepairService> ObjectRepair { get; } = new();
        public Mock<IProgramDataPathPolicyService> PathPolicy { get; } = new();
        public Mock<IProgramDataObjectProvisioner> ObjectProvisioner { get; } = new();
        public Mock<IProgramDataKnownPathResolver> PathResolver { get; } = new();
    }
}
