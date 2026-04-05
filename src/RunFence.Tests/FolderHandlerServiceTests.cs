using System.Security.Principal;
using Microsoft.Win32;
using Moq;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Launch;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for FolderHandlerService.
/// Uses a temp registry key under HKCU as the HKU root, and the current user's SID as the account SID.
/// An injected isAdminAccount delegate controls admin-check behaviour without needing a real admin account.
/// A temp launcher stub is injected for full isolation.
/// </summary>
public class FolderHandlerServiceTests : IDisposable
{
    private readonly string _testSubKey;
    private readonly RegistryKey _hkuRoot;
    private readonly string _testSid;
    private readonly string _tempDir;
    private readonly string _launcherPath;
    private readonly Mock<ILoggingService> _log;
    private readonly Mock<IPermissionGrantService> _permissionGrant;

    public FolderHandlerServiceTests()
    {
        _testSubKey = $@"Software\RunFenceTests\{Guid.NewGuid():N}";
        _hkuRoot = Registry.CurrentUser.CreateSubKey(_testSubKey);
        _testSid = WindowsIdentity.GetCurrent().User!.Value;
        _tempDir = Path.Combine(Path.GetTempPath(), "RunFenceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _launcherPath = Path.Combine(_tempDir, Constants.LauncherExeName);
        File.WriteAllBytes(_launcherPath, []);
        _log = new Mock<ILoggingService>();
        _permissionGrant = new Mock<IPermissionGrantService>();
    }

    public void Dispose()
    {
        _hkuRoot.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_testSubKey, throwOnMissingSubKey: false);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private FolderHandlerService CreateService(bool isAdmin = false, string? shellServerPath = null)
        => new(_log.Object, _permissionGrant.Object,
            hkuOverride: _hkuRoot, launcherPathOverride: _launcherPath,
            isAdminAccount: _ => isAdmin, shellServerPathOverride: shellServerPath);

    private string CommandKeyPath(string classType) =>
        $@"{_testSid}\Software\Classes\{classType}\shell\open\command";

    [Fact]
    public void Register_WritesDirectoryAndFolderCommandKeys()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.Register(_testSid);

        // Assert — both Directory and Folder keys must exist with the correct command
        var expectedCommand = $"\"{_launcherPath}\" --open-folder \"%V\"";

        foreach (var classType in new[] { "Directory", "Folder" })
        {
            using var key = _hkuRoot.OpenSubKey(CommandKeyPath(classType));
            Assert.NotNull(key);
            var command = key.GetValue(null) as string;
            Assert.Equal(expectedCommand, command);
        }
    }

    [Fact]
    public void Register_IsRegistered_ReturnsTrueAfterRegister()
    {
        // Arrange
        var service = CreateService();
        Assert.False(service.IsRegistered(_testSid));

        // Act
        service.Register(_testSid);

        // Assert
        Assert.True(service.IsRegistered(_testSid));
    }

    [Fact]
    public void Register_IsIdempotent_DoesNotRegisterTwice()
    {
        // Arrange
        var service = CreateService();

        // Act — register twice
        service.Register(_testSid);
        service.Register(_testSid);

        // Assert — only logged once (idempotent)
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("registration complete"))), Times.Once);
    }

    [Fact]
    public void Register_SkipsAdminAccounts()
    {
        // Arrange — isAdminAccount always returns true
        var service = CreateService(isAdmin: true);

        // Act
        service.Register(_testSid);

        // Assert — no keys written
        using var key = _hkuRoot.OpenSubKey(CommandKeyPath("Directory"));
        Assert.Null(key);
        Assert.False(service.IsRegistered(_testSid));
    }

    [Fact]
    public void Register_WhenLauncherMissing_DoesNothing()
    {
        // Arrange — launcher path does not exist
        var missingLauncher = Path.Combine(_tempDir, "nonexistent_" + Guid.NewGuid().ToString("N") + ".exe");
        var service = new FolderHandlerService(_log.Object, _permissionGrant.Object,
            hkuOverride: _hkuRoot, launcherPathOverride: missingLauncher, isAdminAccount: _ => false);

        // Act
        service.Register(_testSid);

        // Assert
        using var key = _hkuRoot.OpenSubKey(CommandKeyPath("Directory"));
        Assert.Null(key);
        Assert.False(service.IsRegistered(_testSid));
    }

    [Fact]
    public void Unregister_RemovesCommandKeys()
    {
        // Arrange
        var service = CreateService();
        service.Register(_testSid);
        Assert.True(service.IsRegistered(_testSid));

        // Act
        service.Unregister(_testSid);

        // Assert — both command subtrees removed
        foreach (var classType in new[] { "Directory", "Folder" })
        {
            using var key = _hkuRoot.OpenSubKey(CommandKeyPath(classType));
            Assert.Null(key);
        }

        Assert.False(service.IsRegistered(_testSid));
    }

    [Fact]
    public void Unregister_WhenNotRegistered_DoesNotThrow()
    {
        var service = CreateService();
        var exception = Record.Exception(() => service.Unregister(_testSid));
        Assert.Null(exception);
    }

    [Fact]
    public void CleanupStaleRegistrations_RemovesStaleClsidOverride()
    {
        // Arrange — plant a stale CLSID registration pointing to our shell server
        var shellServerPath = Path.Combine(_tempDir, Constants.ShellServerExeName);
        File.WriteAllBytes(shellServerPath, []);
        var service = CreateService(shellServerPath: shellServerPath);

        var clsidKeyPath = $@"{_testSid}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}\LocalServer32";
        using (var key = _hkuRoot.CreateSubKey(clsidKeyPath))
            key.SetValue(null, $"\"{shellServerPath}\"");

        // Act
        service.CleanupStaleRegistrations();

        // Assert
        var parentClsidPath = $@"{_testSid}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}";
        using var resultKey = _hkuRoot.OpenSubKey(parentClsidPath);
        Assert.Null(resultKey);
    }

    [Fact]
    public void CleanupStaleRegistrations_RemovesStaleClsidOverrideFromLauncherPath()
    {
        // Arrange — plant a stale CLSID registration pointing to the launcher (from old approach)
        var service = CreateService();

        var clsidKeyPath = $@"{_testSid}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}\LocalServer32";
        using (var key = _hkuRoot.CreateSubKey(clsidKeyPath))
            key.SetValue(null, $"\"{_launcherPath}\" --shell-windows");

        // Act
        service.CleanupStaleRegistrations();

        // Assert
        var parentClsidPath = $@"{_testSid}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}";
        using var resultKey = _hkuRoot.OpenSubKey(parentClsidPath);
        Assert.Null(resultKey);
    }

    [Fact]
    public void UnregisterAll_UnregistersAllRegisteredSids()
    {
        // Arrange — register two different SIDs using separate override SIDs
        var sid2 = "S-1-5-21-99999-99999-99999-1001"; // fake SID for second account
        var service = CreateService();
        service.Register(_testSid);

        // Manually register the second SID by writing the registry key directly so the service tracks it
        var commandValue = $"\"{_launcherPath}\" --open-folder \"%V\"";
        using (var key = _hkuRoot.CreateSubKey($@"{sid2}\Software\Classes\Directory\shell\open\command"))
            key.SetValue(null, commandValue);
        using (var key = _hkuRoot.CreateSubKey($@"{sid2}\Software\Classes\Folder\shell\open\command"))
            key.SetValue(null, commandValue);

        // Register via a second service instance that shares the same HKU root
        var service2 = new FolderHandlerService(_log.Object, _permissionGrant.Object,
            hkuOverride: _hkuRoot, launcherPathOverride: _launcherPath, isAdminAccount: _ => false);
        service2.Register(sid2);

        // Act
        service.UnregisterAll();
        service2.UnregisterAll();

        // Assert — both command key trees removed
        foreach (var sid in new[] { _testSid, sid2 })
        {
            using var key = _hkuRoot.OpenSubKey($@"{sid}\Software\Classes\Directory\shell\open\command");
            Assert.Null(key);
        }
    }
}