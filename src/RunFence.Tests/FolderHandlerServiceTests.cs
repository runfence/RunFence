using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using Xunit;

namespace RunFence.Tests;

public class FolderHandlerServiceTests : IDisposable
{
    private readonly string _testSubKey;
    private readonly RegistryKey _hkuRoot;
    private readonly string _testSid;
    private readonly string _tempDir;
    private readonly string _launcherPath;
    private readonly string _unregisterScriptPath;
    private readonly Mock<ILoggingService> _log;
    private readonly Mock<IPathGrantService> _pathGrant;
    private readonly Mock<ILocalGroupMembershipService> _localGroupMembership;

    public FolderHandlerServiceTests()
    {
        _testSubKey = $@"Software\RunFenceTests\{Guid.NewGuid():N}";
        _hkuRoot = Registry.CurrentUser.CreateSubKey(_testSubKey);
        _testSid = WindowsIdentity.GetCurrent().User!.Value;
        _tempDir = Path.Combine(Path.GetTempPath(), "RunFenceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _launcherPath = Path.Combine(_tempDir, PathConstants.LauncherExeName);
        _unregisterScriptPath = Path.Combine(_tempDir, PathConstants.FolderHandlerUnregisterScriptName);
        File.WriteAllBytes(_launcherPath, []);
        _log = new Mock<ILoggingService>();
        _pathGrant = new Mock<IPathGrantService>();
        _localGroupMembership = new Mock<ILocalGroupMembershipService>();
        _localGroupMembership.Setup(s => s.GetGroupsForUser(It.IsAny<string>())).Returns([]);
    }

    public void Dispose()
    {
        _hkuRoot.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_testSubKey, throwOnMissingSubKey: false);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private FolderHandlerService CreateService(
        bool isAdmin = false,
        string? shellServerPath = null,
        string? launcherPath = null)
    {
        _localGroupMembership.Setup(s => s.GetGroupsForUser(It.IsAny<string>()))
            .Returns(isAdmin ? [new("Administrators", "S-1-5-32-544")] : []);

        var effectiveLauncherPath = launcherPath ?? _launcherPath;
        var registryStore = new FolderHandlerRegistryStore(
            _log.Object,
            hkuOverride: _hkuRoot,
            launcherPathOverride: effectiveLauncherPath,
            shellServerPathOverride: shellServerPath);
        var rollback = new FolderHandlerRegistrationRollback(_log.Object, _pathGrant.Object, registryStore);
        return new FolderHandlerService(
            _log.Object,
            _pathGrant.Object,
            _localGroupMembership.Object,
            registryStore,
            rollback,
            new FolderHandlerSidLockProvider(),
            launcherPathOverride: effectiveLauncherPath);
    }

    private string CommandKeyPath(string classType) =>
        $@"{_testSid}\Software\Classes\{classType}\shell\open\command";

    [Fact]
    public void Register_WritesDirectoryAndFolderCommandKeys()
    {
        var service = CreateService();

        service.Register(_testSid);

        var expectedCommand = $"\"{_launcherPath}\" --open-folder \"%V\"";
        foreach (var classType in new[] { "Directory", "Folder" })
        {
            using var key = _hkuRoot.OpenSubKey(CommandKeyPath(classType));
            Assert.NotNull(key);
            Assert.Equal(expectedCommand, key.GetValue(null) as string);
        }
    }

    [Fact]
    public void Register_StoresDirectoryShellFallbackBeforeSettingDefaultVerb()
    {
        using (var directoryShellKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell"))
            directoryShellKey.SetValue(null, "custom");
        var service = CreateService();

        service.Register(_testSid);

        using var resultKey = _hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell");
        Assert.Equal("open", resultKey!.GetValue(null) as string);
        Assert.Equal("custom", resultKey.GetValue(PathConstants.RunFenceFallbackValueName) as string);
    }

    [Fact]
    public void Register_DoesNotOverwriteExistingDirectoryShellFallback()
    {
        using (var directoryShellKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell"))
        {
            directoryShellKey.SetValue(null, "custom");
            directoryShellKey.SetValue(PathConstants.RunFenceFallbackValueName, "previous");
        }
        var service = CreateService();

        service.Register(_testSid);

        using var resultKey = _hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell");
        Assert.Equal("open", resultKey!.GetValue(null) as string);
        Assert.Equal("previous", resultKey.GetValue(PathConstants.RunFenceFallbackValueName) as string);
    }

    [Fact]
    public void Register_WithExistingOpenDefault_RestoresOpenDefaultOnUnregister()
    {
        using (var directoryShellKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell"))
            directoryShellKey.SetValue(null, "open");
        var service = CreateService();

        service.Register(_testSid);
        service.Unregister(_testSid);

        using var resultKey = _hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell");
        Assert.NotNull(resultKey);
        Assert.Equal("open", resultKey.GetValue(null) as string);
        Assert.Null(resultKey.GetValue(PathConstants.RunFenceFallbackValueName));
    }

    [Fact]
    public void Register_IsRegistered_ReturnsTrueAfterRegister()
    {
        var service = CreateService();
        Assert.False(service.IsRegistered(_testSid));

        service.Register(_testSid);

        Assert.True(service.IsRegistered(_testSid));
    }

    [Fact]
    public void Register_EnsuresLauncherDirectoryAccessForAccountAndLowIntegrity()
    {
        var service = CreateService();

        service.Register(_testSid);

        _pathGrant.Verify(g => g.EnsureAccess(
            _testSid, _tempDir, FileSystemRights.ReadAndExecute,
            null, true), Times.Once);
        _pathGrant.Verify(g => g.EnsureAccess(
            AclHelper.LowIntegritySid, _tempDir, FileSystemRights.ReadAndExecute,
            null, true), Times.Once);
    }

    [Fact]
    public void Register_IsIdempotent_DoesNotRegisterTwice()
    {
        var service = CreateService();

        service.Register(_testSid);
        service.Register(_testSid);

        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("registration complete"))), Times.Once);
    }

    [Fact]
    public async Task Register_ConcurrentSameSid_IsSerializedAndIdempotent()
    {
        var service = CreateService();

        await Task.WhenAll(
            Task.Run(() => service.Register(_testSid)),
            Task.Run(() => service.Register(_testSid)));

        _pathGrant.Verify(g => g.EnsureAccess(
            _testSid, _tempDir, FileSystemRights.ReadAndExecute,
            null, true), Times.Once);
        _pathGrant.Verify(g => g.EnsureAccess(
            AclHelper.LowIntegritySid, _tempDir, FileSystemRights.ReadAndExecute,
            null, true), Times.Once);
    }

    [Fact]
    public void Register_SkipsAdminAccounts()
    {
        var service = CreateService(isAdmin: true);

        service.Register(_testSid);

        using var key = _hkuRoot.OpenSubKey(CommandKeyPath("Directory"));
        Assert.Null(key);
        Assert.False(service.IsRegistered(_testSid));
    }

    [Fact]
    public void Register_WhenLauncherMissing_DoesNothing()
    {
        var missingLauncher = Path.Combine(_tempDir, "nonexistent_" + Guid.NewGuid().ToString("N") + ".exe");
        var service = CreateService(launcherPath: missingLauncher);

        service.Register(_testSid);

        using var key = _hkuRoot.OpenSubKey(CommandKeyPath("Directory"));
        Assert.Null(key);
        Assert.False(service.IsRegistered(_testSid));
    }

    [Fact]
    public void Register_WritesRunOnce_WhenUnregisterScriptExists()
    {
        File.WriteAllText(_unregisterScriptPath, "@echo off");
        var service = CreateService();

        service.Register(_testSid);

        using var key = _hkuRoot.OpenSubKey(
            $@"{_testSid}\Software\Microsoft\Windows\CurrentVersion\RunOnce");
        var value = key?.GetValue(PathConstants.FolderHandlerRunOnceValueName) as string;
        Assert.Equal($"cmd /c \"\"{_unregisterScriptPath}\"\"", value);
    }

    [Fact]
    public void Register_SkipsRunOnce_WhenUnregisterScriptMissing()
    {
        var service = CreateService();

        service.Register(_testSid);

        using var key = _hkuRoot.OpenSubKey(
            $@"{_testSid}\Software\Microsoft\Windows\CurrentVersion\RunOnce");
        Assert.Null(key?.GetValue(PathConstants.FolderHandlerRunOnceValueName));
    }

    [Fact]
    public void Unregister_RemovesCommandKeysAndRunOnce()
    {
        File.WriteAllText(_unregisterScriptPath, "@echo off");
        var service = CreateService();
        service.Register(_testSid);
        Assert.True(service.IsRegistered(_testSid));

        service.Unregister(_testSid);

        foreach (var classType in new[] { "Directory", "Folder" })
        {
            using var key = _hkuRoot.OpenSubKey(CommandKeyPath(classType));
            Assert.Null(key);
        }

        using var runOnceKey = _hkuRoot.OpenSubKey(
            $@"{_testSid}\Software\Microsoft\Windows\CurrentVersion\RunOnce");
        Assert.Null(runOnceKey?.GetValue(PathConstants.FolderHandlerRunOnceValueName));
        Assert.False(service.IsRegistered(_testSid));
    }

    [Fact]
    public void Unregister_RestoresDirectoryShellFallback_WhenDefaultVerbIsOwned()
    {
        using (var customVerbKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell\custom\command"))
            customVerbKey.SetValue(null, @"""C:\Other\App.exe"" ""%1""");
        using (var directoryShellKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell"))
            directoryShellKey.SetValue(null, "custom");
        var service = CreateService();
        service.Register(_testSid);

        service.Unregister(_testSid);

        using var resultKey = _hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell");
        Assert.NotNull(resultKey);
        Assert.Equal("custom", resultKey.GetValue(null) as string);
        Assert.Null(resultKey.GetValue(PathConstants.RunFenceFallbackValueName));
        Assert.NotNull(_hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell\custom\command"));
    }

    [Fact]
    public void Unregister_DeletesDirectoryShellDefault_WhenFallbackWasEmptyAndDefaultVerbIsOwned()
    {
        var service = CreateService();
        service.Register(_testSid);

        service.Unregister(_testSid);

        using var resultKey = _hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell");
        Assert.True(resultKey == null || resultKey.GetValue(null) == null);
        Assert.Null(resultKey?.GetValue(PathConstants.RunFenceFallbackValueName));
    }

    [Fact]
    public void Unregister_PreservesSharedVerbParent_WhenOwnedCommandLeafIsRemoved()
    {
        var service = CreateService();
        service.Register(_testSid);

        using (var customVerbKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell\custom\command"))
        {
            customVerbKey.SetValue(null, @"""C:\Other\App.exe"" ""%1""");
        }

        using (var directoryShellKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell"))
        {
            directoryShellKey.SetValue(null, "custom");
        }

        service.Unregister(_testSid);

        Assert.Null(_hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell\open\command"));
        using var directoryShellResultKey = _hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell");
        Assert.NotNull(directoryShellResultKey);
        Assert.Equal("custom", directoryShellResultKey.GetValue(null) as string);
        Assert.NotNull(_hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell\custom\command"));
    }

    [Fact]
    public void CleanupStaleRegistrations_RestoresDirectoryShellFallback_WhenDefaultVerbIsOwned()
    {
        using (var customVerbKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell\custom\command"))
            customVerbKey.SetValue(null, @"""C:\Other\App.exe"" ""%1""");
        using (var directoryShellKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell"))
        {
            directoryShellKey.SetValue(null, "open");
            directoryShellKey.SetValue(PathConstants.RunFenceFallbackValueName, "custom");
        }
        using (var ownedOpenCommand = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell\open\command"))
            ownedOpenCommand.SetValue(null, $"\"{_launcherPath}\" --open-folder \"%V\"");
        var service = CreateService();

        service.CleanupStaleRegistrations();

        using var resultKey = _hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell");
        Assert.NotNull(resultKey);
        Assert.Equal("custom", resultKey.GetValue(null) as string);
        Assert.Null(resultKey.GetValue(PathConstants.RunFenceFallbackValueName));
        Assert.Null(_hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell\open\command"));
    }

    [Fact]
    public void CleanupStaleRegistrations_RestoresDirectoryShellFallback_WhenExploreDefaultVerbIsOwned()
    {
        using (var customVerbKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell\custom\command"))
            customVerbKey.SetValue(null, @"""C:\Other\App.exe"" ""%1""");
        using (var directoryShellKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell"))
        {
            directoryShellKey.SetValue(null, "explore");
            directoryShellKey.SetValue(PathConstants.RunFenceFallbackValueName, "custom");
        }
        using (var ownedExploreCommand = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell\explore\command"))
            ownedExploreCommand.SetValue(null, $"\"{_launcherPath}\" --open-folder \"%V\"");
        var service = CreateService();

        service.CleanupStaleRegistrations();

        using var resultKey = _hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell");
        Assert.NotNull(resultKey);
        Assert.Equal("custom", resultKey.GetValue(null) as string);
        Assert.Null(resultKey.GetValue(PathConstants.RunFenceFallbackValueName));
        Assert.Null(_hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell\explore\command"));
        Assert.NotNull(_hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell\custom\command"));
    }

    [Fact]
    public void CleanupStaleRegistrations_DeletesDirectoryShellFallbackWithoutRestoring_WhenDefaultVerbIsForeign()
    {
        using (var customVerbKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell\custom\command"))
            customVerbKey.SetValue(null, @"""C:\Other\App.exe"" ""%1""");
        using (var directoryShellKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell"))
        {
            directoryShellKey.SetValue(null, "custom");
            directoryShellKey.SetValue(PathConstants.RunFenceFallbackValueName, "previous");
        }
        using (var ownedOpenCommand = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell\open\command"))
            ownedOpenCommand.SetValue(null, $"\"{_launcherPath}\" --open-folder \"%V\"");
        var service = CreateService();

        service.CleanupStaleRegistrations();

        using var resultKey = _hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell");
        Assert.NotNull(resultKey);
        Assert.Equal("custom", resultKey.GetValue(null) as string);
        Assert.Null(resultKey.GetValue(PathConstants.RunFenceFallbackValueName));
        Assert.Null(_hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell\open\command"));
        Assert.NotNull(_hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell\custom\command"));
    }

    [Fact]
    public void CleanupStaleRegistrations_DeletesStaleDirectoryShellFallback_WhenNoOwnedCommandRemains()
    {
        using (var customVerbKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell\custom\command"))
            customVerbKey.SetValue(null, @"""C:\Other\App.exe"" ""%1""");
        using (var directoryShellKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell"))
        {
            directoryShellKey.SetValue(null, "custom");
            directoryShellKey.SetValue(PathConstants.RunFenceFallbackValueName, "previous");
        }
        var service = CreateService();

        service.CleanupStaleRegistrations();

        using var resultKey = _hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell");
        Assert.NotNull(resultKey);
        Assert.Equal("custom", resultKey.GetValue(null) as string);
        Assert.Null(resultKey.GetValue(PathConstants.RunFenceFallbackValueName));
        Assert.NotNull(_hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell\custom\command"));
    }

    [Fact]
    public void CleanupStaleRegistrations_RestoresDirectoryShellFallback_WhenOpenDefaultCommandIsAlreadyMissing()
    {
        using (var customVerbKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell\custom\command"))
            customVerbKey.SetValue(null, @"""C:\Other\App.exe"" ""%1""");
        using (var directoryShellKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell"))
        {
            directoryShellKey.SetValue(null, "open");
            directoryShellKey.SetValue(PathConstants.RunFenceFallbackValueName, "custom");
        }
        var service = CreateService();

        service.CleanupStaleRegistrations();

        using var resultKey = _hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell");
        Assert.NotNull(resultKey);
        Assert.Equal("custom", resultKey.GetValue(null) as string);
        Assert.Null(resultKey.GetValue(PathConstants.RunFenceFallbackValueName));
        Assert.NotNull(_hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell\custom\command"));
    }

    [Fact]
    public void CleanupStaleRegistrations_RestoresDirectoryShellFallback_WhenOpenDefaultCommandValueIsMissing()
    {
        using (var customVerbKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell\custom\command"))
            customVerbKey.SetValue(null, @"""C:\Other\App.exe"" ""%1""");
        _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell\open\command")?.Dispose();
        using (var directoryShellKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell"))
        {
            directoryShellKey.SetValue(null, "open");
            directoryShellKey.SetValue(PathConstants.RunFenceFallbackValueName, "custom");
        }
        var service = CreateService();

        service.CleanupStaleRegistrations();

        using var resultKey = _hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell");
        Assert.NotNull(resultKey);
        Assert.Equal("custom", resultKey.GetValue(null) as string);
        Assert.Null(resultKey.GetValue(PathConstants.RunFenceFallbackValueName));
        Assert.NotNull(_hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell\custom\command"));
    }

    [Fact]
    public void CleanupStaleRegistrations_DeletesDirectoryShellDefault_WhenOwnedDefaultHasNoFallback()
    {
        using (var directoryShellKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell"))
            directoryShellKey.SetValue(null, "open");
        using (var ownedOpenCommand = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell\open\command"))
            ownedOpenCommand.SetValue(null, $"\"{_launcherPath}\" --open-folder \"%V\"");
        var service = CreateService();

        service.CleanupStaleRegistrations();

        using var resultKey = _hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell");
        Assert.True(resultKey == null || resultKey.GetValue(null) == null);
        Assert.Null(_hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell\open\command"));
    }

    [Fact]
    public void CleanupStaleRegistrations_DeletesDirectoryShellFallbackWithoutRestoring_WhenOpenDefaultCommandIsForeign()
    {
        using (var foreignOpenKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell\open\command"))
            foreignOpenKey.SetValue(null, @"""C:\Other\App.exe"" ""%1""");
        using (var directoryShellKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell"))
        {
            directoryShellKey.SetValue(null, "open");
            directoryShellKey.SetValue(PathConstants.RunFenceFallbackValueName, "custom");
        }
        var service = CreateService();

        service.CleanupStaleRegistrations();

        using var resultKey = _hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell");
        Assert.NotNull(resultKey);
        Assert.Equal("open", resultKey.GetValue(null) as string);
        Assert.Null(resultKey.GetValue(PathConstants.RunFenceFallbackValueName));
        Assert.Equal(
            @"""C:\Other\App.exe"" ""%1""",
            _hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell\open\command")!.GetValue(null) as string);
    }

    [Fact]
    public void CleanupStaleRegistrations_DeletesStaleDirectoryShellFallback_WhenDirectoryShellBecomesEmpty()
    {
        using (var directoryShellKey = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell"))
            directoryShellKey.SetValue(PathConstants.RunFenceFallbackValueName, "previous");
        var service = CreateService();

        service.CleanupStaleRegistrations();

        Assert.Null(_hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell"));
    }

    [Fact]
    public void Unregister_PreservesForeignCommandKeys()
    {
        var service = CreateService();
        service.Register(_testSid);

        using (var foreignDirectoryCommand = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell\open\command"))
        {
            foreignDirectoryCommand.SetValue(null, @"""C:\Other\App.exe"" ""%1""");
        }

        using (var foreignFolderCommand = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Folder\shell\open\command"))
        {
            foreignFolderCommand.SetValue(null, @"""C:\Other\FolderApp.exe"" ""%1""");
        }

        service.Unregister(_testSid);

        Assert.Equal(
            @"""C:\Other\App.exe"" ""%1""",
            _hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Directory\shell\open\command")?.GetValue(null) as string);
        Assert.Equal(
            @"""C:\Other\FolderApp.exe"" ""%1""",
            _hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes\Folder\shell\open\command")?.GetValue(null) as string);
        Assert.False(service.IsRegistered(_testSid));
    }

    [Fact]
    public void Unregister_PreservesForeignHandlerKeys_WithoutKeepingTrackedRegistration()
    {
        var service = CreateService();
        service.Register(_testSid);

        using (var foreignDirectoryOpen = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell\open\command"))
        {
            foreignDirectoryOpen.SetValue(null, @"""C:\Other\App.exe"" ""%1""");
        }

        using (var foreignDirectoryExplore = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Directory\shell\explore\command"))
        {
            foreignDirectoryExplore.SetValue(null, @"""C:\Other\App.exe"" ""%1""");
        }

        using (var foreignFolderOpen = _hkuRoot.CreateSubKey($@"{_testSid}\Software\Classes\Folder\shell\open\command"))
        {
            foreignFolderOpen.SetValue(null, @"""C:\Other\FolderApp.exe"" ""%1""");
        }

        service.Unregister(_testSid);

        Assert.False(service.IsRegistered(_testSid));
    }

    [Fact]
    public void Unregister_RemovesOnlyOwnedClsidOverride()
    {
        var shellServerPath = Path.Combine(_tempDir, PathConstants.ShellServerExeName);
        File.WriteAllBytes(shellServerPath, []);
        var service = CreateService(shellServerPath: shellServerPath);

        using (var localServerKey = _hkuRoot.CreateSubKey(
                   $@"{_testSid}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}\LocalServer32"))
        {
            localServerKey.SetValue(null, $"\"{shellServerPath}\"");
        }

        using (var clsidKey = _hkuRoot.CreateSubKey($@"{_testSid}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}"))
        {
            clsidKey.SetValue("SharedValue", "keep");
        }

        service.Unregister(_testSid);

        Assert.Null(_hkuRoot.OpenSubKey($@"{_testSid}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}\LocalServer32"));
        Assert.NotNull(_hkuRoot.OpenSubKey($@"{_testSid}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}"));
    }

    [Fact]
    public void Unregister_PreservesForeignClsidOverride()
    {
        var service = CreateService();

        using (var localServerKey = _hkuRoot.CreateSubKey(
                   $@"{_testSid}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}\LocalServer32"))
        {
            localServerKey.SetValue(null, @"""C:\Other\ShellServer.exe""");
        }

        service.Unregister(_testSid);

        Assert.NotNull(_hkuRoot.OpenSubKey($@"{_testSid}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}\LocalServer32"));
    }

    [Fact]
    public void UnregisterScript_UsesManagedLauncherCleanupPath()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "RunFence",
            PathConstants.FolderHandlerUnregisterScriptName));
        var installerPublishPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "installer",
            "publish",
            PathConstants.FolderHandlerUnregisterScriptName));

        const string expected = "@echo off\n\"%~dp0RunFence.Launcher.exe\" --unregister-folder-handler\n";

        Assert.Equal(expected, NormalizeLineEndings(File.ReadAllText(sourcePath)));
        Assert.Equal(expected, NormalizeLineEndings(File.ReadAllText(installerPublishPath)));
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
        var shellServerPath = Path.Combine(_tempDir, PathConstants.ShellServerExeName);
        File.WriteAllBytes(shellServerPath, []);
        var service = CreateService(shellServerPath: shellServerPath);

        var clsidKeyPath = $@"{_testSid}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}\LocalServer32";
        using (var key = _hkuRoot.CreateSubKey(clsidKeyPath))
            key.SetValue(null, $"\"{shellServerPath}\"");

        service.CleanupStaleRegistrations();

        var parentClsidPath = $@"{_testSid}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}";
        using var resultKey = _hkuRoot.OpenSubKey(parentClsidPath);
        Assert.Null(resultKey);
    }

    [Fact]
    public void CleanupStaleRegistrations_RemovesStaleClsidCommandLeafAndPrunesEmptyParents()
    {
        var service = CreateService();

        using (var commandKey = _hkuRoot.CreateSubKey(
                   $@"{_testSid}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}\shell\open\command"))
        {
            commandKey.SetValue(null, $"\"{_launcherPath}\" --shell-windows");
        }

        service.CleanupStaleRegistrations();

        Assert.Null(_hkuRoot.OpenSubKey($@"{_testSid}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}\shell\open\command"));
        Assert.Null(_hkuRoot.OpenSubKey($@"{_testSid}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}\shell\open"));
        Assert.Null(_hkuRoot.OpenSubKey($@"{_testSid}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}\shell"));
        Assert.Null(_hkuRoot.OpenSubKey($@"{_testSid}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}"));
    }

    [Fact]
    public void CleanupStaleRegistrations_RemovesStaleClsidOverrideFromLauncherPath()
    {
        var service = CreateService();
        var clsidKeyPath = $@"{_testSid}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}\LocalServer32";
        using (var key = _hkuRoot.CreateSubKey(clsidKeyPath))
            key.SetValue(null, $"\"{_launcherPath}\" --shell-windows");

        service.CleanupStaleRegistrations();

        var parentClsidPath = $@"{_testSid}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}";
        using var resultKey = _hkuRoot.OpenSubKey(parentClsidPath);
        Assert.Null(resultKey);
    }

    [Fact]
    public void UnregisterAll_UnregistersOnlySidsTrackedByThisInstance()
    {
        var sid2 = "S-1-5-21-99999-99999-99999-1001";
        var service = CreateService();
        service.Register(_testSid);

        var service2 = CreateService();
        service2.Register(sid2);

        service.UnregisterAll();
        service2.UnregisterAll();

        foreach (var sid in new[] { _testSid, sid2 })
        {
            using var key = _hkuRoot.OpenSubKey($@"{sid}\Software\Classes\Directory\shell\open\command");
            Assert.Null(key);
        }
    }

    [Fact]
    public void Register_WhenGrantFails_RollsBackRegistryAndRegistrationState()
    {
        _pathGrant.SetupSequence(g => g.EnsureAccess(
                It.IsAny<string>(), It.IsAny<string>(), FileSystemRights.ReadAndExecute, null, true))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true))
            .Throws(CreateGrantFailure(_tempDir, GrantApplyFailureStep.GrantAclApply, "denied"));
        var service = CreateService();

        Assert.Throws<GrantOperationException>(() => service.Register(_testSid));

        using var key = _hkuRoot.OpenSubKey(CommandKeyPath("Directory"));
        Assert.Null(key);
        Assert.False(service.IsRegistered(_testSid));
        _pathGrant.Verify(g => g.RemoveGrant(_testSid, _tempDir, false), Times.Once);
    }

    [Fact]
    public void Rollback_WhenGrantAndTraverseWereApplied_RemovesBothForAccountAndLowIntegrity()
    {
        var registryStore = new FolderHandlerRegistryStore(
            _log.Object,
            hkuOverride: _hkuRoot,
            launcherPathOverride: _launcherPath);
        var rollback = new FolderHandlerRegistrationRollback(_log.Object, _pathGrant.Object, registryStore);
        var effects = new FolderHandlerRegistrationEffects(_testSid, _launcherPath)
        {
            AccountGrantApplied = true,
            AccountTraverseApplied = true,
            LowIntegrityGrantApplied = true,
            LowIntegrityTraverseApplied = true
        };

        rollback.Rollback(effects);

        _pathGrant.Verify(g => g.RemoveGrant(_testSid, _tempDir, false), Times.Once);
        _pathGrant.Verify(g => g.RemoveTraverse(_testSid, _tempDir), Times.Once);
        _pathGrant.Verify(g => g.RemoveGrant(AclHelper.LowIntegritySid, _tempDir, false), Times.Once);
        _pathGrant.Verify(g => g.RemoveTraverse(AclHelper.LowIntegritySid, _tempDir), Times.Once);
    }

    [Fact]
    public void Register_WhenGrantSaveFails_ThrowsWarningAndKeepsRegistration()
    {
        _pathGrant.SetupSequence(g => g.EnsureAccess(
                It.IsAny<string>(), It.IsAny<string>(), FileSystemRights.ReadAndExecute, null, true))
            .Throws(CreateGrantFailure(_tempDir, GrantApplyFailureStep.GrantIntentSave, "save failed"))
            .Returns(default(GrantApplyResult));
        var service = CreateService();

        var exception = Assert.Throws<InvalidOperationException>(() => service.Register(_testSid));

        Assert.Contains("warning", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("save failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(service.IsRegistered(_testSid));
        using var key = _hkuRoot.OpenSubKey(CommandKeyPath("Directory"));
        Assert.NotNull(key);
        _pathGrant.Verify(g => g.RemoveGrant(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        using var runOnceKey = _hkuRoot.OpenSubKey(
            $@"{_testSid}\Software\Microsoft\Windows\CurrentVersion\RunOnce");
        Assert.Null(runOnceKey?.GetValue(PathConstants.FolderHandlerRunOnceValueName));
    }

    [Fact]
    public void Register_WhenEnsureAccessReturnsWarnings_IncludesFormattedWarnings()
    {
        var warning = new GrantApplyWarning(
            GrantApplyFailureStep.PostGrantMutationSave,
            _tempDir,
            @"C:\configs\account.rfn",
            new InvalidOperationException("account save warning"));
        _pathGrant.SetupSequence(g => g.EnsureAccess(
                It.IsAny<string>(), It.IsAny<string>(), FileSystemRights.ReadAndExecute, null, true))
            .Returns(new GrantApplyResult(
                GrantApplied: true,
                DatabaseModified: true,
                DurableSaveCompleted: false,
                Warnings: [warning]))
            .Returns(default(GrantApplyResult));
        var service = CreateService();

        var result = service.Register(_testSid);

        Assert.Equal([GrantApplyFailureFormatter.Format(warning)], result.Warnings);
        Assert.True(service.IsRegistered(_testSid));
    }

    [Fact]
    public void Register_WhenAccountAndLowIntegrityEnsureAccessReturnWarnings_PreservesRegistrationOrder()
    {
        var accountWarning = new GrantApplyWarning(
            GrantApplyFailureStep.PostGrantMutationSave,
            _tempDir,
            @"C:\configs\account.rfn",
            new InvalidOperationException("account warning"));
        var lowIntegrityWarning = new GrantApplyWarning(
            GrantApplyFailureStep.PostTraverseRemoveSave,
            _tempDir,
            @"C:\configs\low-il.rfn",
            new InvalidOperationException("low integrity warning"));
        _pathGrant.SetupSequence(g => g.EnsureAccess(
                It.IsAny<string>(), It.IsAny<string>(), FileSystemRights.ReadAndExecute, null, true))
            .Returns(new GrantApplyResult(
                GrantApplied: true,
                DatabaseModified: true,
                DurableSaveCompleted: false,
                Warnings: [accountWarning]))
            .Returns(new GrantApplyResult(
                GrantApplied: true,
                DatabaseModified: true,
                DurableSaveCompleted: false,
                Warnings: [lowIntegrityWarning]));
        var service = CreateService();

        var result = service.Register(_testSid);

        Assert.Equal(
            [GrantApplyFailureFormatter.Format(accountWarning), GrantApplyFailureFormatter.Format(lowIntegrityWarning)],
            result.Warnings);
        Assert.True(service.IsRegistered(_testSid));
    }

    [Fact]
    public async Task RegisterAndUnregister_ConcurrentCalls_FinalStateIsConsistent()
    {
        var service = CreateService();

        var register = Task.Run(() => service.Register(_testSid));
        var unregister = Task.Run(() => service.Unregister(_testSid));
        await Task.WhenAll(register, unregister);

        using var key = _hkuRoot.OpenSubKey(CommandKeyPath("Directory"));
        var registered = service.IsRegistered(_testSid);
        Assert.Equal(registered, key != null);
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n");
    }

    private static GrantOperationException CreateGrantFailure(string path, GrantApplyFailureStep step, string message)
        => new(step, path, null, new InvalidOperationException(message));
}
