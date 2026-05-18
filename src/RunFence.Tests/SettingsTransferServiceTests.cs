using System.Security.AccessControl;
using Moq;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.PrefTrans;
using Xunit;

namespace RunFence.Tests;

public class SettingsTransferServiceTests
{
    private const string FakeInteractiveSid = "S-1-5-21-1000-2000-3000-1001";
    private static readonly string SharedTempRoot = Path.Combine(PathConstants.ProgramDataDir, "temp");

    private readonly Mock<IPrefTransLauncher> _launcher;
    private readonly Mock<ISettingsTransferAccessGrantService> _accessGrantService;
    private readonly Mock<ISettingsTransferStagingService> _stagingService;
    private readonly Mock<IInteractiveUserResolver> _interactiveUserResolver;
    private readonly Mock<ILoggingService> _log;

    public SettingsTransferServiceTests()
    {
        _log = new Mock<ILoggingService>();
        _launcher = new Mock<IPrefTransLauncher>();
        _accessGrantService = new Mock<ISettingsTransferAccessGrantService>(MockBehavior.Strict);
        _stagingService = new Mock<ISettingsTransferStagingService>(MockBehavior.Strict);
        _interactiveUserResolver = new Mock<IInteractiveUserResolver>();

        _accessGrantService
            .Setup(g => g.CleanupTemporaryGrant());
    }

    private sealed record FakePrefTransFixture(
        TempDirectory TempDir,
        string FakePrefTransPath,
        SettingsTransferService Service) : IDisposable
    {
        public void Dispose() => TempDir.Dispose();
    }

    private FakePrefTransFixture CreateServiceWithFakePrefTrans()
    {
        var tempDir = new TempDirectory("SettingsTransfer");
        var fakePrefTrans = Path.Combine(tempDir.Path, "preftrans.exe");
        File.WriteAllText(fakePrefTrans, string.Empty);
        var service = CreateService(tempDir.Path);
        return new FakePrefTransFixture(tempDir, fakePrefTrans, service);
    }

    private SettingsTransferService CreateService(string baseDirectory)
    {
        return new SettingsTransferService(
            _log.Object,
            _launcher.Object,
            () => _accessGrantService.Object,
            _stagingService.Object,
            _interactiveUserResolver.Object,
            baseDirectory);
    }

    [Fact]
    public void ValidatePrefTransExists_BuildsPathInBaseDirectory()
    {
        var service = CreateService(AppContext.BaseDirectory);
        service.ValidatePrefTransExists(out var path);

        Assert.Equal("preftrans.exe", Path.GetFileName(path));
        Assert.Equal(AppContext.BaseDirectory, Path.GetDirectoryName(path) + Path.DirectorySeparatorChar);
    }

    [Fact]
    public void ValidatePrefTransExists_ReturnsFalseWhenMissing()
    {
        using var tempDir = new TempDirectory("SettingsTransfer_Missing");
        var service = CreateService(tempDir.Path);

        var result = service.ValidatePrefTransExists(out _);

        Assert.False(result);
    }

    [Fact]
    public void ExportDesktopSettings_ReturnsFailureWhenInteractiveUserSessionMissing()
    {
        _interactiveUserResolver.Setup(r => r.GetInteractiveUserSid()).Returns((string?)null);

        using var fx = CreateServiceWithFakePrefTrans();

        var result = fx.Service.ExportDesktopSettings("C:\\settings.json");

        Assert.False(result.Success);
        Assert.Contains("interactive user session", result.Message, StringComparison.OrdinalIgnoreCase);
        _launcher.VerifyNoOtherCalls();
        _stagingService.VerifyNoOtherCalls();
        _accessGrantService.VerifyNoOtherCalls();
    }

    [Fact]
    public void Import_ReturnsFailureWhenPrefTransMissing()
    {
        using var tempDir = new TempDirectory("SettingsTransfer_NoPrefTrans");
        var service = CreateService(tempDir.Path);
        var settingsPath = Path.Combine(tempDir.Path, "settings.json");
        File.WriteAllText(settingsPath, "{}");

        var result = service.Import(settingsPath, FakeInteractiveSid);

        Assert.False(result.Success);
        Assert.Contains("preftrans.exe not found", result.Message, StringComparison.OrdinalIgnoreCase);
        _accessGrantService.VerifyNoOtherCalls();
        _stagingService.VerifyNoOtherCalls();
        _launcher.VerifyNoOtherCalls();
    }

    [Fact]
    public void ExportDesktopSettings_WhenAccessPreparationFails_ReturnsFailureWithoutLaunching()
    {
        const string outputPath = "C:\\settings.json";
        var currentUserSid = SidResolutionHelper.GetCurrentUserSid()!;
        string? prefTransPath = null;

        _interactiveUserResolver.Setup(r => r.GetInteractiveUserSid()).Returns(currentUserSid);

        using var fx = CreateServiceWithFakePrefTrans();
        prefTransPath = fx.FakePrefTransPath;
        var prefTransDirectory = Path.GetDirectoryName(prefTransPath)!;

        _accessGrantService
            .Setup(g => g.TryEnsureDurableAccess(
                currentUserSid,
                prefTransDirectory,
                FileSystemRights.ReadAndExecute))
            .Throws(new InvalidOperationException("access check failed"));

        var result = fx.Service.ExportDesktopSettings(outputPath);

        Assert.False(result.Success);
        Assert.Contains("access check failed", result.Message, StringComparison.OrdinalIgnoreCase);
        _launcher.VerifyNoOtherCalls();
        _stagingService.VerifyNoOtherCalls();
        _accessGrantService.Verify(g => g.TryEnsureAccess(
            currentUserSid,
            prefTransDirectory,
            FileSystemRights.ReadAndExecute,
            true), Times.Never);
        _accessGrantService.Verify(g => g.TryEnsureDurableAccess(
            currentUserSid,
            prefTransDirectory,
            FileSystemRights.ReadAndExecute), Times.Once);
        _accessGrantService.Verify(g => g.TryEnsureAccess(
            currentUserSid,
            outputPath,
            FileSystemRights.Write | FileSystemRights.Synchronize,
            false), Times.Never);
        _accessGrantService.Verify(g => g.CleanupTemporaryGrant(), Times.Once);
    }

    [Fact]
    public void ExportDesktopSettings_DirectRoute_UsesOutputFile()
    {
        const string outputPath = "C:\\settings.json";
        var currentUserSid = SidResolutionHelper.GetCurrentUserSid()!;
        string? capturedMode = null;
        string? capturedFile = null;

        _interactiveUserResolver.Setup(r => r.GetInteractiveUserSid()).Returns(currentUserSid);

        using var fx = CreateServiceWithFakePrefTrans();
        var prefTransDirectory = Path.GetDirectoryName(fx.FakePrefTransPath)!;
        _accessGrantService
            .Setup(g => g.TryEnsureDurableAccess(currentUserSid, prefTransDirectory, FileSystemRights.ReadAndExecute))
            .Returns(new SettingsTransferGrantResult(true, true, null));
        _accessGrantService
            .Setup(g => g.TryEnsureAccess(currentUserSid, outputPath, FileSystemRights.Write | FileSystemRights.Synchronize, false))
            .Returns(new SettingsTransferGrantResult(true, true, null));
        _stagingService
            .Setup(s => s.CreateSharedTempDirectoryPath())
            .Throws(new Exception("Shared temp path should not be requested"));
        _launcher.Setup(l => l.RunAndWait(
                fx.FakePrefTransPath,
                "store",
                outputPath,
                currentUserSid,
                30_000,
                null))
            .Callback<string, string, string, string, int, Action?>((_, mode, path, _, _, _) =>
            {
                capturedMode = mode;
                capturedFile = path;
            })
            .Returns(new SettingsTransferResult(true, ""));

        var result = fx.Service.ExportDesktopSettings(outputPath);

        Assert.True(result.Success);
        Assert.Equal("store", capturedMode, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(outputPath, capturedFile);
        Assert.True(result.DatabaseModified);
        _accessGrantService.Verify(g => g.TryEnsureDurableAccess(
            currentUserSid,
            prefTransDirectory,
            FileSystemRights.ReadAndExecute), Times.Once);
        _accessGrantService.Verify(g => g.TryEnsureAccess(
            currentUserSid,
            outputPath,
            FileSystemRights.Write | FileSystemRights.Synchronize,
            false), Times.Once);
        _accessGrantService.Verify(g => g.CleanupTemporaryGrant(), Times.Once);
        _stagingService.VerifyNoOtherCalls();
    }

    [Fact]
    public void ExportDesktopSettings_TemporaryRoute_UsesRestrictedTempAndCopiesBack()
    {
        using var outputDirectory = new TempDirectory("SettingsTransferOutput");
        var outputPath = Path.Combine(outputDirectory.Path, "settings-export.json");
        string? temporaryPath = null;
        string? tempCopySource = null;
        string? tempCopyDestination = null;
        var tempDirectory = Path.Combine(SharedTempRoot, "export-case");
        var stagedFile = Path.Combine(tempDirectory, "settings.json");

        _interactiveUserResolver.Setup(r => r.GetInteractiveUserSid()).Returns(FakeInteractiveSid);
        using var fx = CreateServiceWithFakePrefTrans();
        var exeDirectory = Path.GetDirectoryName(fx.FakePrefTransPath)!;

        _stagingService.Setup(s => s.CreateSharedTempDirectoryPath()).Returns(tempDirectory);
        _stagingService
            .Setup(s => s.CreateRestrictedExportDirectory(tempDirectory, FakeInteractiveSid))
            .Returns(tempDirectory);
        _stagingService
            .Setup(s => s.CopyExportFileToDestination(stagedFile, outputPath))
            .Callback<string, string>((source, destination) =>
            {
                tempCopySource = source;
                tempCopyDestination = destination;
                File.Copy(source, destination, overwrite: true);
            });
        _stagingService
            .Setup(s => s.TryDeleteTempDirectory(tempDirectory))
            .Returns(tempDirectory);

        _accessGrantService
            .Setup(g => g.TryEnsureDurableAccess(FakeInteractiveSid, exeDirectory, FileSystemRights.ReadAndExecute))
            .Returns(new SettingsTransferGrantResult(true, true, null));

        _launcher.Setup(l => l.RunAndWait(
                fx.FakePrefTransPath,
                "store",
                stagedFile,
                FakeInteractiveSid,
                30_000,
                null))
            .Callback<string, string, string, string, int, Action?>((_, _, actualStagedPath, _, _, _) =>
            {
                temporaryPath = actualStagedPath;
                Directory.CreateDirectory(Path.GetDirectoryName(stagedFile)!);
                File.WriteAllText(stagedFile, "{\"ok\":true}");
            })
            .Returns(new SettingsTransferResult(true, ""));

        var result = fx.Service.ExportDesktopSettings(outputPath);

        Assert.True(result.Success);
        Assert.NotNull(temporaryPath);
        Assert.Equal(stagedFile, temporaryPath);
        Assert.Equal(stagedFile, tempCopySource);
        Assert.Equal(outputPath, tempCopyDestination);
        Assert.Equal("{\"ok\":true}", File.ReadAllText(outputPath));
        Assert.True(result.DatabaseModified);
        _stagingService.Verify(s => s.CreateSharedTempDirectoryPath(), Times.Once);
        _stagingService.Verify(s => s.CreateRestrictedExportDirectory(tempDirectory, FakeInteractiveSid), Times.Once);
        _stagingService.Verify(s => s.CopyExportFileToDestination(stagedFile, outputPath), Times.Once);
        _stagingService.Verify(s => s.TryDeleteTempDirectory(tempDirectory), Times.Once);
        _stagingService.Verify(s => s.TryDeleteTempFile(stagedFile), Times.Never);
        _accessGrantService.Verify(g => g.TryEnsureDurableAccess(
            FakeInteractiveSid,
            exeDirectory,
            FileSystemRights.ReadAndExecute), Times.Once);
        _accessGrantService.Verify(g => g.TryEnsureAccess(
            FakeInteractiveSid,
            outputPath,
            FileSystemRights.Read | FileSystemRights.Synchronize,
            false), Times.Never);
        _accessGrantService.Verify(g => g.CleanupTemporaryGrant(), Times.Once);
    }

    [Fact]
    public void ExportDesktopSettings_TemporaryRoute_WhenAccessPreparationFails_ReturnsFailureWithoutLaunching()
    {
        const string outputPath = "C:\\settings.json";
        var tempDirectory = Path.Combine(SharedTempRoot, "export-failure");

        _interactiveUserResolver.Setup(r => r.GetInteractiveUserSid()).Returns(FakeInteractiveSid);
        using var fx = CreateServiceWithFakePrefTrans();
        var exeDirectory = Path.GetDirectoryName(fx.FakePrefTransPath)!;

        _stagingService
            .Setup(s => s.CreateSharedTempDirectoryPath())
            .Returns(tempDirectory);
        _stagingService
            .Setup(s => s.CreateRestrictedExportDirectory(tempDirectory, FakeInteractiveSid))
            .Throws(new InvalidOperationException("restricted export directory failed"));
        _stagingService
            .Setup(s => s.TryDeleteTempDirectory(tempDirectory))
            .Returns(tempDirectory);

        _accessGrantService
            .Setup(g => g.TryEnsureDurableAccess(
                FakeInteractiveSid,
                exeDirectory,
                FileSystemRights.ReadAndExecute))
            .Returns(new SettingsTransferGrantResult(true, true, null));

        var result = fx.Service.ExportDesktopSettings(outputPath);

        Assert.False(result.Success);
        Assert.Contains("restricted export directory failed", result.Message, StringComparison.OrdinalIgnoreCase);
        _stagingService.Verify(s => s.CreateSharedTempDirectoryPath(), Times.Once);
        _stagingService.Verify(s => s.CreateRestrictedExportDirectory(tempDirectory, FakeInteractiveSid), Times.Once);
        _launcher.VerifyNoOtherCalls();
        _accessGrantService.Verify(g => g.TryEnsureDurableAccess(
            FakeInteractiveSid,
            exeDirectory,
            FileSystemRights.ReadAndExecute), Times.Never);
        _accessGrantService.Verify(g => g.CleanupTemporaryGrant(), Times.Once);
        _stagingService.Verify(s => s.TryDeleteTempDirectory(tempDirectory), Times.Once);
    }

    [Fact]
    public void Import_DirectRoute_UsesSourceDirectlyWhenPossible()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid()!;
        string? capturedMode = null;
        string? capturedPath = null;

        _interactiveUserResolver.Setup(r => r.GetInteractiveUserSid()).Returns(currentSid);

        using var fx = CreateServiceWithFakePrefTrans();
        var settingsFile = Path.Combine(fx.TempDir.Path, "settings.json");
        File.WriteAllText(settingsFile, "{}");

        var exeDirectory = Path.GetDirectoryName(fx.FakePrefTransPath)!;
        _accessGrantService
            .Setup(g => g.TryEnsureDurableAccess(currentSid, exeDirectory, FileSystemRights.ReadAndExecute))
            .Returns(new SettingsTransferGrantResult(true, true, null));
        _accessGrantService
            .Setup(g => g.TryEnsureAccess(currentSid, settingsFile, FileSystemRights.Read | FileSystemRights.Synchronize, false))
            .Returns(new SettingsTransferGrantResult(true, true, null));
        _launcher.Setup(l => l.RunAndWait(
                fx.FakePrefTransPath,
                "load",
                settingsFile,
                currentSid,
                60_000,
                null))
            .Callback<string, string, string, string, int, Action?>((_, mode, path, _, _, _) =>
            {
                capturedMode = mode;
                capturedPath = path;
            })
            .Returns(new SettingsTransferResult(true, ""));

        var result = fx.Service.Import(settingsFile, currentSid);

        Assert.True(result.Success);
        Assert.Equal("load", capturedMode, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(settingsFile, capturedPath);
        Assert.True(result.DatabaseModified);
        _accessGrantService.Verify(g => g.TryEnsureDurableAccess(currentSid, exeDirectory, FileSystemRights.ReadAndExecute), Times.Once);
        _accessGrantService.Verify(g => g.TryEnsureAccess(currentSid, settingsFile, FileSystemRights.Read | FileSystemRights.Synchronize, false), Times.Once);
        _accessGrantService.Verify(g => g.CleanupTemporaryGrant(), Times.Once);
        _accessGrantService.Verify(
            g => g.TryEnsureAccessForCleanup(
                currentSid,
                settingsFile,
                FileSystemRights.Read | FileSystemRights.Synchronize,
                false),
            Times.Never);
        _stagingService.VerifyNoOtherCalls();
    }

    [Fact]
    public void Import_WhenAccessPreparationFails_ReturnsFailureWithoutLaunching()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid()!;

        _interactiveUserResolver.Setup(r => r.GetInteractiveUserSid()).Returns(currentSid);
        using var fx = CreateServiceWithFakePrefTrans();

        var settingsFile = Path.Combine(fx.TempDir.Path, "settings.json");
        File.WriteAllText(settingsFile, "{}");
        var exeDirectory = Path.GetDirectoryName(fx.FakePrefTransPath)!;

        _accessGrantService
            .Setup(g => g.TryEnsureDurableAccess(
                currentSid,
                exeDirectory,
                FileSystemRights.ReadAndExecute))
            .Throws(new InvalidOperationException("access check failed"));

        var result = fx.Service.Import(settingsFile, currentSid);

        Assert.False(result.Success);
        Assert.Contains("access check failed", result.Message, StringComparison.OrdinalIgnoreCase);
        _launcher.VerifyNoOtherCalls();
        _stagingService.VerifyNoOtherCalls();
        _accessGrantService.Verify(g => g.TryEnsureDurableAccess(
            currentSid,
            exeDirectory,
            FileSystemRights.ReadAndExecute), Times.Once);
        _accessGrantService.Verify(g => g.TryEnsureAccess(
            currentSid,
            settingsFile,
            FileSystemRights.Read | FileSystemRights.Synchronize,
            false), Times.Never);
        _accessGrantService.Verify(g => g.CleanupTemporaryGrant(), Times.Once);
        _stagingService.VerifyNoOtherCalls();
    }

    [Fact]
    public void Import_TemporaryRoute_UsesRestrictedTempCopy()
    {
        const string accountSid = "S-1-5-21-5555-5555-5555-5000";
        var stagedFile = Path.Combine(SharedTempRoot, "import-case", "settings.json");
        var stagedDirectory = Path.GetDirectoryName(stagedFile)!;
        string? capturedPath = null;
        var settingsFile = string.Empty;

        _interactiveUserResolver.Setup(r => r.GetInteractiveUserSid()).Returns(FakeInteractiveSid);

        using var fx = CreateServiceWithFakePrefTrans();
        settingsFile = Path.Combine(fx.TempDir.Path, "settings.json");
        File.WriteAllText(settingsFile, "{}");
        var exeDirectory = Path.GetDirectoryName(fx.FakePrefTransPath)!;

        _stagingService
            .Setup(s => s.CreateSharedTempFilePath("json"))
            .Returns(stagedFile);
        _stagingService
            .Setup(s => s.CopyImportFileToRestrictedTemp(settingsFile, stagedFile, FakeInteractiveSid))
            .Returns(stagedFile);
        _stagingService
            .Setup(s => s.TryDeleteTempFile(stagedFile))
            .Returns(stagedFile);
        _stagingService
            .Setup(s => s.TryDeleteTempDirectory(stagedDirectory))
            .Returns(stagedDirectory);
        _accessGrantService
            .Setup(g => g.TryEnsureDurableAccess(accountSid, exeDirectory, FileSystemRights.ReadAndExecute))
            .Returns(new SettingsTransferGrantResult(true, true, null));
        _accessGrantService
            .Setup(g => g.TryEnsureAccessForCleanup(
                accountSid,
                stagedFile,
                FileSystemRights.Read | FileSystemRights.Synchronize,
                false))
            .Returns(new SettingsTransferGrantResult(true, true, null));
        _launcher.Setup(l => l.RunAndWait(
                fx.FakePrefTransPath,
                "load",
                stagedFile,
                accountSid,
                60_000,
                null))
            .Callback<string, string, string, string, int, Action?>((_, _, path, _, _, _) => capturedPath = path)
            .Returns(new SettingsTransferResult(true, ""));

        var result = fx.Service.Import(settingsFile, accountSid);

        Assert.True(result.Success);
        Assert.Equal(stagedFile, capturedPath);
        Assert.NotEqual(settingsFile, capturedPath);
        Assert.True(result.DatabaseModified);
        _stagingService.Verify(s => s.CreateSharedTempFilePath("json"), Times.Once);
        _stagingService.Verify(s => s.CopyImportFileToRestrictedTemp(settingsFile, stagedFile, FakeInteractiveSid), Times.Once);
        _stagingService.Verify(s => s.TryDeleteTempDirectory(stagedDirectory), Times.Once);
        _stagingService.Verify(s => s.TryDeleteTempFile(stagedFile), Times.Once);
        _accessGrantService.Verify(g => g.TryEnsureDurableAccess(accountSid, exeDirectory, FileSystemRights.ReadAndExecute), Times.Once);
        _accessGrantService.Verify(g => g.TryEnsureAccessForCleanup(
            accountSid,
            stagedFile,
            FileSystemRights.Read | FileSystemRights.Synchronize,
            false), Times.Once);
        _accessGrantService.Verify(g => g.CleanupTemporaryGrant(), Times.Once);
        _launcher.Verify(l => l.RunAndWait(
            fx.FakePrefTransPath,
            "load",
            stagedFile,
            accountSid,
            60_000,
            null), Times.Once);
    }

    [Fact]
    public void Import_TemporaryRoute_WhenCleanupAccessPreparationFails_ReturnsFailureWithoutLaunching()
    {
        const string accountSid = "S-1-5-21-5555-5555-5555-5000";
        var stagedFile = Path.Combine(SharedTempRoot, "cleanup-failure", "settings.json");
        var stagedDirectory = Path.GetDirectoryName(stagedFile)!;

        _interactiveUserResolver.Setup(r => r.GetInteractiveUserSid()).Returns(FakeInteractiveSid);

        using var fx = CreateServiceWithFakePrefTrans();
        var settingsFile = Path.Combine(fx.TempDir.Path, "settings.json");
        File.WriteAllText(settingsFile, "{}");
        var exeDirectory = Path.GetDirectoryName(fx.FakePrefTransPath)!;

        _stagingService
            .Setup(s => s.CreateSharedTempFilePath("json"))
            .Returns(stagedFile);
        _stagingService
            .Setup(s => s.CopyImportFileToRestrictedTemp(settingsFile, stagedFile, FakeInteractiveSid))
            .Returns(stagedFile);
        _stagingService
            .Setup(s => s.TryDeleteTempFile(stagedFile))
            .Returns(stagedFile);
        _stagingService
            .Setup(s => s.TryDeleteTempDirectory(stagedDirectory))
            .Returns(stagedDirectory);
        _accessGrantService
            .Setup(g => g.TryEnsureDurableAccess(accountSid, exeDirectory, FileSystemRights.ReadAndExecute))
            .Returns(new SettingsTransferGrantResult(true, true, null));
        _accessGrantService
            .Setup(g => g.TryEnsureAccessForCleanup(
                accountSid,
                stagedFile,
                FileSystemRights.Read | FileSystemRights.Synchronize,
                false))
            .Throws(new InvalidOperationException("cleanup access failed"));

        var result = fx.Service.Import(settingsFile, accountSid);

        Assert.False(result.Success);
        Assert.Contains("cleanup access failed", result.Message, StringComparison.OrdinalIgnoreCase);
        _launcher.VerifyNoOtherCalls();
        _accessGrantService.Verify(g => g.TryEnsureDurableAccess(accountSid, exeDirectory, FileSystemRights.ReadAndExecute), Times.Once);
        _accessGrantService.Verify(g => g.TryEnsureAccessForCleanup(
            accountSid,
            stagedFile,
            FileSystemRights.Read | FileSystemRights.Synchronize,
            false), Times.Once);
        _stagingService.Verify(s => s.TryDeleteTempDirectory(stagedDirectory), Times.Once);
        _stagingService.Verify(s => s.TryDeleteTempFile(stagedFile), Times.Once);
        _accessGrantService.Verify(g => g.CleanupTemporaryGrant(), Times.Once);
    }

    [Fact]
    public void Import_TemporaryRoute_WhenInteractiveUserSessionMissing_ReturnsFailureWithoutLaunching()
    {
        const string accountSid = "S-1-5-21-5555-5555-5555-5000";

        _interactiveUserResolver.Setup(r => r.GetInteractiveUserSid()).Returns((string?)null);
        _launcher.VerifyNoOtherCalls();

        using var fx = CreateServiceWithFakePrefTrans();
        var settingsFile = Path.Combine(fx.TempDir.Path, "settings.json");
        File.WriteAllText(settingsFile, "{}");

        var result = fx.Service.Import(settingsFile, accountSid);

        Assert.False(result.Success);
        Assert.Contains("interactive user session", result.Message, StringComparison.OrdinalIgnoreCase);
        _launcher.VerifyNoOtherCalls();
        _accessGrantService.VerifyNoOtherCalls();
        _stagingService.VerifyNoOtherCalls();
    }

    [Fact]
    public void ExportDesktopSettings_PreservesLauncherDatabaseModified()
    {
        const string outputPath = "C:\\settings.json";
        var currentSid = SidResolutionHelper.GetCurrentUserSid()!;

        _interactiveUserResolver.Setup(r => r.GetInteractiveUserSid()).Returns(currentSid);

        using var fx = CreateServiceWithFakePrefTrans();
        var exeDirectory = Path.GetDirectoryName(fx.FakePrefTransPath)!;
        _accessGrantService
            .Setup(g => g.TryEnsureDurableAccess(currentSid, exeDirectory, FileSystemRights.ReadAndExecute))
            .Returns(new SettingsTransferGrantResult(true, false, null));
        _accessGrantService
            .Setup(g => g.TryEnsureAccess(currentSid, outputPath, FileSystemRights.Write | FileSystemRights.Synchronize, false))
            .Returns(new SettingsTransferGrantResult(true, false, null));
        _launcher.Setup(l => l.RunAndWait(
                fx.FakePrefTransPath,
                "store",
                outputPath,
                currentSid,
                30_000,
                null))
            .Returns(new SettingsTransferResult(true, "", DatabaseModified: true));

        var result = fx.Service.ExportDesktopSettings(outputPath);

        Assert.True(result.Success);
        Assert.True(result.DatabaseModified);
        _accessGrantService.Verify(g => g.TryEnsureDurableAccess(currentSid, exeDirectory, FileSystemRights.ReadAndExecute), Times.Once);
        _accessGrantService.Verify(g => g.TryEnsureAccess(currentSid, outputPath, FileSystemRights.Write | FileSystemRights.Synchronize, false), Times.Once);
    }
}
