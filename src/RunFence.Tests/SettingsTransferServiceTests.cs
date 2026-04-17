using System.Security.AccessControl;
using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.PrefTrans;
using Xunit;

namespace RunFence.Tests;

public class SettingsTransferServiceTests
{
    private readonly SettingsTransferService _service;
    private readonly Mock<IPrefTransLauncher> _launcher;

    public SettingsTransferServiceTests()
    {
        var log = new Mock<ILoggingService>();
        _launcher = new Mock<IPrefTransLauncher>();
        var pathGrantService = new Mock<IPathGrantService>();
        _service = new SettingsTransferService(log.Object, _launcher.Object, pathGrantService.Object);
    }

    private sealed record FakePrefTransFixture(
        TempDirectory TempDir,
        string FakePrefTransPath,
        Mock<IPrefTransLauncher> Launcher,
        Mock<IPathGrantService> PathGrantService,
        SettingsTransferService Service) : IDisposable
    {
        public void Dispose() => TempDir.Dispose();
    }

    private static FakePrefTransFixture CreateServiceWithFakePrefTrans(
        Mock<IPrefTransLauncher>? launcher = null,
        Mock<IPathGrantService>? pathGrantService = null)
    {
        var tempDir = new TempDirectory("SettingsTransfer");
        var fakePrefTrans = Path.Combine(tempDir.Path, "preftrans.exe");
        File.WriteAllText(fakePrefTrans, "");

        launcher ??= new Mock<IPrefTransLauncher>();
        pathGrantService ??= new Mock<IPathGrantService>();

        var log = new Mock<ILoggingService>();
        var service = new SettingsTransferService(log.Object, launcher.Object, pathGrantService.Object)
        {
            BaseDirectory = tempDir.Path
        };

        return new FakePrefTransFixture(tempDir, fakePrefTrans, launcher, pathGrantService, service);
    }

    // --- ValidatePrefTransExists tests ---

    [Fact]
    public void ValidatePrefTransExists_BuildsPathInBaseDirectory()
    {
        // Validates path construction — the returned path always points to preftrans.exe
        // in AppContext.BaseDirectory, regardless of whether the file exists.
        _service.ValidatePrefTransExists(out var path);

        Assert.Equal("preftrans.exe", Path.GetFileName(path));
        Assert.Equal(AppContext.BaseDirectory, Path.GetDirectoryName(path) + Path.DirectorySeparatorChar);
    }

    [Fact]
    public void ValidatePrefTransExists_ReturnsFalseWhenMissing()
    {
        using var tempDir = new TempDirectory("SettingsTransfer_Missing");
        var log = new Mock<ILoggingService>();
        var launcher = new Mock<IPrefTransLauncher>();
        var pathGrantService = new Mock<IPathGrantService>();
        var service = new SettingsTransferService(log.Object, launcher.Object, pathGrantService.Object)
        {
            BaseDirectory = tempDir.Path
        };

        var result = service.ValidatePrefTransExists(out _);

        Assert.False(result);
    }

    // --- Operations with invalid inputs always fail ---

    [Fact]
    public void ExportDesktopSettings_InvalidPath_LauncherReturnsFailure()
    {
        // When the launcher returns failure, SettingsTransferService propagates it.
        _launcher.Setup(l => l.RunAndWait(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Action?>()))
            .Returns(new SettingsTransferResult(false, "File path contains characters that are not safe for command execution."));

        using var fx = CreateServiceWithFakePrefTrans(_launcher);

        var result = fx.Service.ExportDesktopSettings(@"C:\temp\out&bad.json");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public void Import_LauncherReturnsFailure_PropagatesFailure()
    {
        // When the launcher returns failure, SettingsTransferService propagates it.
        _launcher.Setup(l => l.RunAndWait(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Action?>()))
            .Returns(new SettingsTransferResult(false, "File path contains characters that are not safe for command execution."));

        using var fx = CreateServiceWithFakePrefTrans(_launcher);
        var settingsFile = Path.Combine(fx.TempDir.Path, "settings&bad.json");
        File.WriteAllText(settingsFile, "{}");

        var result = fx.Service.Import(settingsFile, accountSid: "S-1-5-21-0-0-0-1000");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Message);
    }

    // --- Export: preftrans process launch ---

    [Fact]
    public void ExportDesktopSettings_LauncherCalledWithExpectedArguments()
    {
        // Arrange
        string? capturedPrefTrans = null;
        string? capturedMode = null;
        string? capturedFilePath = null;

        var launcher = new Mock<IPrefTransLauncher>();
        launcher.Setup(l => l.RunAndWait(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Action?>()))
            .Callback<string, string, string, string, int, Action?>((exe, mode, file, sid, _, _) =>
            {
                capturedPrefTrans = exe;
                capturedMode = mode;
                capturedFilePath = file;
            })
            .Returns(new SettingsTransferResult(true, ""));

        using var fx = CreateServiceWithFakePrefTrans(launcher);
        var outputPath = Path.Combine(fx.TempDir.Path, "out.json");

        // Act
        var result = fx.Service.ExportDesktopSettings(outputPath);

        // Assert: launcher called with correct exe path and output file
        Assert.True(result.Success);
        Assert.Equal(fx.FakePrefTransPath, capturedPrefTrans);
        Assert.Equal("store", capturedMode, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(outputPath, capturedFilePath);
    }

    [Fact]
    public void Import_LauncherSuccess_ReturnsSuccess()
    {
        // Arrange
        var launcher = new Mock<IPrefTransLauncher>();
        launcher.Setup(l => l.RunAndWait(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Action?>()))
            .Returns(new SettingsTransferResult(true, ""));

        using var fx = CreateServiceWithFakePrefTrans(launcher);
        var settingsFile = Path.Combine(fx.TempDir.Path, "settings.json");
        File.WriteAllText(settingsFile, "{}");

        // Act
        var result = fx.Service.Import(settingsFile, accountSid: "S-1-5-21-0-0-0-1000");

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public void Import_LauncherCalledWithLoadModeAndAccountSid()
    {
        // Verifies that Import passes command="load" and the accountSid to the preftrans launcher.
        // Arrange
        const string accountSid = "S-1-5-21-0-0-0-1000";
        string? capturedMode = null;
        string? capturedSid = null;

        var launcher = new Mock<IPrefTransLauncher>();
        launcher.Setup(l => l.RunAndWait(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Action?>()))
            .Callback<string, string, string, string, int, Action?>((_, mode, _, sid, _, _) =>
            {
                capturedMode = mode;
                capturedSid = sid;
            })
            .Returns(new SettingsTransferResult(true, ""));

        using var fx = CreateServiceWithFakePrefTrans(launcher);
        var settingsFile = Path.Combine(fx.TempDir.Path, "settings.json");
        File.WriteAllText(settingsFile, "{}");

        // Act
        var result = fx.Service.Import(settingsFile, accountSid: accountSid);

        // Assert: launcher called with "load" mode and the correct account SID
        Assert.True(result.Success);
        Assert.Equal("load", capturedMode, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(accountSid, capturedSid, StringComparer.OrdinalIgnoreCase);
    }

    // --- Import (credentials path) file error ---

    [Fact]
    public void Import_Credentials_NonexistentSourceFile_ReturnsFailure()
    {
        var result = _service.Import(@"C:\nonexistent\no_such_file.json",
            accountSid: "S-1-5-21-0-0-0-1000");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Message);
    }

    // --- Import (credentials path) permission grant integration ---

    [Fact]
    public void Import_Credentials_PermissionGrantApplied_DatabaseModifiedReturned()
    {
        // Arrange: create a real temp dir with a dummy preftrans.exe and settings file
        var mockGrant = new Mock<IPathGrantService>();
        mockGrant.Setup(g => g.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(),
                It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(GrantAdded: true, TraverseAdded: false, DatabaseModified: true));

        using var fx = CreateServiceWithFakePrefTrans(pathGrantService: mockGrant);
        var settingsFile = Path.Combine(fx.TempDir.Path, "settings.json");
        File.WriteAllText(settingsFile, "{}");

        // Act
        var result = fx.Service.Import(settingsFile, accountSid: "S-1-5-21-0-0-0-1000");

        // Assert: DatabaseModified reflects that grants were applied
        Assert.True(result.DatabaseModified);
    }

    [Fact]
    public void Import_Credentials_EnsureAccessThrowsOce_ReturnsDeclinedResult()
    {
        // Arrange
        var mockGrant = new Mock<IPathGrantService>();
        // Simulate EnsureAccess throwing OperationCanceledException (e.g. confirm callback cancelled)
        mockGrant.Setup(g => g.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(),
                It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Throws(new OperationCanceledException());

        using var fx = CreateServiceWithFakePrefTrans(pathGrantService: mockGrant);
        var settingsFile = Path.Combine(fx.TempDir.Path, "settings.json");
        File.WriteAllText(settingsFile, "{}");

        // Act
        var result = fx.Service.Import(settingsFile, accountSid: "S-1-5-21-0-0-0-1000");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("declined", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Import_Credentials_EnsureAccessCalledForExeDirAndSettingsFile()
    {
        // Arrange
        var mockGrant = new Mock<IPathGrantService>();

        using var fx = CreateServiceWithFakePrefTrans(pathGrantService: mockGrant);
        var settingsFile = Path.Combine(fx.TempDir.Path, "settings.json");
        File.WriteAllText(settingsFile, "{}");

        // Act
        fx.Service.Import(settingsFile, accountSid: "S-1-5-21-0-0-0-1000");

        // Assert: EnsureAccess called for the exe directory (inlined from EnsureExeDirectoryAccess)
        // and for the settings file.
        mockGrant.Verify(g => g.EnsureAccess(
            It.IsAny<string>(), fx.TempDir.Path, // exe directory = BaseDirectory (where preftrans.exe lives)
            FileSystemRights.ReadAndExecute,
            It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()), Times.Once);
        mockGrant.Verify(g => g.EnsureAccess(
            It.IsAny<string>(), settingsFile,
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()), Times.Once);
    }
}
