using System.Security;
using System.Security.AccessControl;
using Moq;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Launch;
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
        var permissionGrant = new Mock<IPermissionGrantService>();
        _service = new SettingsTransferService(log.Object, _launcher.Object, permissionGrant.Object);
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
        var log = new Mock<ILoggingService>();
        var launcher = new Mock<IPrefTransLauncher>();
        var permissionGrant = new Mock<IPermissionGrantService>();
        var service = new SettingsTransferService(log.Object, launcher.Object, permissionGrant.Object,
            baseDirectory: Path.GetTempPath());

        var result = service.ValidatePrefTransExists(out _);

        Assert.False(result);
    }

    // --- Operations with invalid inputs always fail ---

    [Fact]
    public void ExportDesktopSettings_InvalidPath_LauncherReturnsFailure()
    {
        // When the launcher returns failure, SettingsTransferService propagates it.
        _launcher.Setup(l => l.RunAndWait(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<LaunchCredentials>(), It.IsAny<int>(), It.IsAny<Action?>()))
            .Returns(new SettingsTransferResult(false, "File path contains characters that are not safe for command execution."));

        using var tempDir = new TempDirectory();
        var fakePrefTrans = Path.Combine(tempDir.Path, "preftrans.exe");
        File.WriteAllText(fakePrefTrans, "");

        var log = new Mock<ILoggingService>();
        var permissionGrant = new Mock<IPermissionGrantService>();
        var service = new SettingsTransferService(log.Object, _launcher.Object, permissionGrant.Object,
            baseDirectory: tempDir.Path);

        var result = service.ExportDesktopSettings(@"C:\temp\out&bad.json");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public void Import_CurrentAccount_InvalidPath_LauncherReturnsFailure()
    {
        // When the launcher returns failure, SettingsTransferService propagates it.
        _launcher.Setup(l => l.RunAndWait(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<LaunchCredentials>(), It.IsAny<int>(), It.IsAny<Action?>()))
            .Returns(new SettingsTransferResult(false, "File path contains characters that are not safe for command execution."));

        using var tempDir = new TempDirectory();
        var fakePrefTrans = Path.Combine(tempDir.Path, "preftrans.exe");
        var settingsFile = Path.Combine(tempDir.Path, "settings&bad.json");
        File.WriteAllText(fakePrefTrans, "");
        File.WriteAllText(settingsFile, "{}");

        var log = new Mock<ILoggingService>();
        var permissionGrant = new Mock<IPermissionGrantService>();
        var service = new SettingsTransferService(log.Object, _launcher.Object, permissionGrant.Object,
            baseDirectory: tempDir.Path);

        var result = service.Import(settingsFile,
            new LaunchCredentials(null, null, null, LaunchTokenSource.CurrentProcess),
            accountSid: "S-1-5-21-0-0-0-1000");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Message);
    }

    // --- Import (credentials path) file error ---

    [Fact]
    public void Import_Credentials_NonexistentSourceFile_ReturnsFailure()
    {
        using var password = new SecureString();
        var result = _service.Import(@"C:\nonexistent\no_such_file.json",
            new LaunchCredentials(password, "DOMAIN", "user1"),
            accountSid: "S-1-5-21-0-0-0-1000");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Message);
    }

    // --- Import (credentials path) permission grant integration ---

    [Fact]
    public void Import_Credentials_PermissionGrantApplied_DatabaseModifiedReturned()
    {
        // Arrange: create a real temp dir with a dummy preftrans.exe and settings file
        using var tempDir = new TempDirectory();
        var fakePrefTrans = Path.Combine(tempDir.Path, "preftrans.exe");
        var settingsFile = Path.Combine(tempDir.Path, "settings.json");
        File.WriteAllText(fakePrefTrans, "");
        File.WriteAllText(settingsFile, "{}");

        var log = new Mock<ILoggingService>();
        var mockGrant = new Mock<IPermissionGrantService>();
        mockGrant.Setup(g => g.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(),
                It.IsAny<Func<string, string, bool>?>()))
            .Returns(new EnsureAccessResult(GrantAdded: true, DatabaseModified: true));

        var launcher = new Mock<IPrefTransLauncher>();
        var service = new SettingsTransferService(log.Object, launcher.Object, mockGrant.Object,
            baseDirectory: tempDir.Path);
        using var password = new SecureString();

        // Act
        var result = service.Import(settingsFile,
            new LaunchCredentials(password, "DOMAIN", "user1"),
            accountSid: "S-1-5-21-0-0-0-1000");

        // Assert: DatabaseModified reflects that grants were applied
        Assert.True(result.DatabaseModified);
    }

    [Fact]
    public void Import_Credentials_EnsureAccessThrowsOce_ReturnsDeclinedResult()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var fakePrefTrans = Path.Combine(tempDir.Path, "preftrans.exe");
        var settingsFile = Path.Combine(tempDir.Path, "settings.json");
        File.WriteAllText(fakePrefTrans, "");
        File.WriteAllText(settingsFile, "{}");

        var log = new Mock<ILoggingService>();
        var mockGrant = new Mock<IPermissionGrantService>();
        // Simulate EnsureAccess throwing OperationCanceledException (e.g. confirm callback cancelled)
        mockGrant.Setup(g => g.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(),
                It.IsAny<Func<string, string, bool>?>()))
            .Throws(new OperationCanceledException());

        var launcher = new Mock<IPrefTransLauncher>();
        var service = new SettingsTransferService(log.Object, launcher.Object, mockGrant.Object,
            baseDirectory: tempDir.Path);
        using var password = new SecureString();

        // Act
        var result = service.Import(settingsFile,
            new LaunchCredentials(password, "DOMAIN", "user1"),
            accountSid: "S-1-5-21-0-0-0-1000");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("declined", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Import_Credentials_EnsureExeDirectoryAccess_CalledForPrefTrans_EnsureAccess_CalledForSettingsFile()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var fakePrefTrans = Path.Combine(tempDir.Path, "preftrans.exe");
        var settingsFile = Path.Combine(tempDir.Path, "settings.json");
        File.WriteAllText(fakePrefTrans, "");
        File.WriteAllText(settingsFile, "{}");

        var log = new Mock<ILoggingService>();
        var mockGrant = new Mock<IPermissionGrantService>();
        mockGrant.Setup(g => g.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(),
                It.IsAny<Func<string, string, bool>?>()))
            .Returns(new EnsureAccessResult());

        var launcher = new Mock<IPrefTransLauncher>();
        var service = new SettingsTransferService(log.Object, launcher.Object, mockGrant.Object,
            baseDirectory: tempDir.Path);
        using var password = new SecureString();

        // Act
        service.Import(settingsFile,
            new LaunchCredentials(password, "DOMAIN", "user1"),
            accountSid: "S-1-5-21-0-0-0-1000");

        // Assert: EnsureExeDirectoryAccess for preftrans (self-contained, handles grant+traverse).
        // EnsureAccess for the settings file (grant tracked internally via RunOnUiThread).
        mockGrant.Verify(g => g.EnsureExeDirectoryAccess(fakePrefTrans, It.IsAny<string>(), It.IsAny<Func<string, string, bool>?>()), Times.Once);
        mockGrant.Verify(g => g.EnsureAccess(settingsFile, It.IsAny<string>(),
            It.IsAny<FileSystemRights>(),
            It.IsAny<Func<string, string, bool>?>()), Times.Once);
    }
}
