using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Acl;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launching.Resolution;
using Xunit;

namespace RunFence.Tests;

public class ShortcutServiceTests
{
    [Fact]
    public void ParseManagedShortcutArgs_ExactId_ReturnsEmpty()
    {
        var id = AppEntry.GenerateId();

        var result = ShortcutClassificationHelper.ParseManagedShortcutArgs(id, id);
        Assert.Equal("", result);
    }

    [Fact]
    public void ParseManagedShortcutArgs_IdWithArgs_ReturnsOriginalArgs()
    {
        var id = AppEntry.GenerateId();
        var currentArgs = $"{id} --some-arg value";

        var result = ShortcutClassificationHelper.ParseManagedShortcutArgs(currentArgs, id);
        Assert.Equal("--some-arg value", result);
    }

    [Fact]
    public void ParseManagedShortcutArgs_IdWithSpaceOnly_ReturnsEmpty()
    {
        var id = AppEntry.GenerateId();
        var currentArgs = $"{id} ";

        var result = ShortcutClassificationHelper.ParseManagedShortcutArgs(currentArgs, id);
        Assert.Equal("", result);
    }

    [Fact]
    public void ParseManagedShortcutArgs_WrongId_ReturnsNull()
    {
        var id = AppEntry.GenerateId();
        var currentArgs = "something-else --args";

        var result = ShortcutClassificationHelper.ParseManagedShortcutArgs(currentArgs, id);
        Assert.Null(result);
    }

    [Fact]
    public void ParseManagedShortcutArgs_IdWithMultipleSpacedArgs_PreservesArgs()
    {
        var id = AppEntry.GenerateId();
        var originalArgs = "--file \"C:\\My Documents\\test.txt\" --verbose";
        var currentArgs = $"{id} {originalArgs}";

        var result = ShortcutClassificationHelper.ParseManagedShortcutArgs(currentArgs, id);
        Assert.Equal(originalArgs, result);
    }

    [Fact]
    public void ParseManagedShortcutArgs_EmptyArgs_ReturnsNull()
    {
        var id = AppEntry.GenerateId();
        var result = ShortcutClassificationHelper.ParseManagedShortcutArgs("", id);
        Assert.Null(result);
    }

    // --- IsUninstallShortcut tests ---

    [Theory]
    [InlineData("Uninstall MyApp", @"C:\MyApps\app.exe")]
    [InlineData("uninstall myapp", @"C:\MyApps\app.exe")]
    [InlineData("UNINSTALL", @"C:\MyApps\app.exe")]
    public void IsUninstallShortcut_ShortcutNameContainsUninstall_ReturnsTrue(string shortcutName, string target)
    {
        Assert.True(ShortcutClassificationHelper.IsUninstallShortcut(shortcutName + ".lnk", target));
    }

    [Theory]
    [InlineData("MyApp", @"C:\MyApps\unins000.exe")]
    [InlineData("MyApp", @"C:\MyApps\Unins001.exe")]
    [InlineData("MyApp", @"C:\MyApps\UNINSTALLER.exe")]
    public void IsUninstallShortcut_TargetStartsWithUnins_ReturnsTrue(string shortcutName, string target)
    {
        Assert.True(ShortcutClassificationHelper.IsUninstallShortcut(shortcutName + ".lnk", target));
    }

    [Theory]
    [InlineData("MyApp", @"C:\MyApps\myapp.exe")]
    [InlineData("Launch", @"C:\MyApps\setup.exe")]
    [InlineData("Reinstall", @"C:\MyApps\app.exe")]
    public void IsUninstallShortcut_NormalShortcut_ReturnsFalse(string shortcutName, string target)
    {
        Assert.False(ShortcutClassificationHelper.IsUninstallShortcut(shortcutName + ".lnk", target));
    }

    // --- Beside-target shortcut naming tests ---

    [Fact]
    public void GetBesideTargetShortcutName_ExeApp_ReturnsCorrectName()
    {
        var app = new AppEntry { ExePath = @"C:\MyApps\notepad.exe" };
        var name = BesideTargetShortcutService.GetBesideTargetShortcutName(app, "TestUser");
        Assert.Equal("notepad-as-TestUser.lnk", name);
    }

    [Fact]
    public void GetBesideTargetShortcutName_FolderApp_UsesFolderName()
    {
        var app = new AppEntry { ExePath = @"C:\MyApps\GameFolder", IsFolder = true };
        var name = BesideTargetShortcutService.GetBesideTargetShortcutName(app, "Player1");
        Assert.Equal("GameFolder-as-Player1.lnk", name);
    }

    [Fact]
    public void GetBesideTargetShortcutName_SanitizesInvalidChars()
    {
        var app = new AppEntry { ExePath = @"C:\MyApps\test.exe" };
        var name = BesideTargetShortcutService.GetBesideTargetShortcutName(app, "DOMAIN\\User");
        // Backslash is invalid in filenames → replaced with _
        Assert.Equal("test-as-DOMAIN_User.lnk", name);
    }

    [Fact]
    public void GetBesideTargetShortcutPaths_ExeApp_ReturnsSinglePath()
    {
        var app = new AppEntry { ExePath = @"C:\MyApps\test.exe" };
        var paths = BesideTargetShortcutService.GetBesideTargetShortcutPaths(app, "test-as-User.lnk");
        Assert.Single(paths);
        Assert.Equal(Path.GetFullPath(@"C:\MyApps\test-as-User.lnk"), paths[0]);
    }

    [Fact]
    public void GetBesideTargetShortcutPaths_FolderApp_ReturnsTwoPaths()
    {
        var app = new AppEntry { ExePath = @"C:\MyApps\GameFolder", IsFolder = true };
        var paths = BesideTargetShortcutService.GetBesideTargetShortcutPaths(app, "GameFolder-as-User.lnk");
        Assert.Equal(2, paths.Count);
        // First: beside the folder (in parent dir)
        Assert.Equal(Path.GetFullPath(@"C:\MyApps\GameFolder-as-User.lnk"), paths[0]);
        // Second: inside the folder
        Assert.Equal(Path.GetFullPath(@"C:\MyApps\GameFolder\GameFolder-as-User.lnk"), paths[1]);
    }

    [Fact]
    public void CreateBesideTargetShortcut_WindowsAppsApp_DoesNotCreateShortcut()
    {
        var app = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            ExePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "WindowsApps",
                "Contoso.App_1.0.0.0_x64__8wekyb3d8bbwe",
                "App.exe")
        };
        var log = new Mock<ILoggingService>();
        var protection = new Mock<IShortcutProtectionService>();
        var shortcutHelper = new FakeShortcutComHelper(new FakeShortcut());
        var service = new BesideTargetShortcutService(
            log.Object,
            protection.Object,
            new InMemoryShortcutProtectionStateStore(),
            new FakeShortcutGateway(shortcutHelper),
            new FakeShortcutWriteAccessService(shortcutHelper),
            Mock.Of<IManagedShortcutLifecycleService>(),
            WindowsAppsAliasPathResolverFalse(),
            NonUwpKindService(),
            ProgramDataKnownPathResolver());

        service.CreateBesideTargetShortcut(app, @"C:\RunFence\RunFence.Launcher.exe", "", "User");

        Assert.Equal(0, shortcutHelper.WithShortcutCount);
    }

    [Fact]
    public void EnforceBesideTargetShortcuts_WindowsAppsApp_DoesNotCreateShortcut()
    {
        var app = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            Name = "Store App",
            ExePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "WindowsApps",
                "Contoso.App_1.0.0.0_x64__8wekyb3d8bbwe",
                "App.exe")
        };
        var log = new Mock<ILoggingService>();
        var protection = new Mock<IShortcutProtectionService>();
        var shortcutHelper = new FakeShortcutComHelper(new FakeShortcut());
        var service = new BesideTargetShortcutService(
            log.Object,
            protection.Object,
            new InMemoryShortcutProtectionStateStore(),
            new FakeShortcutGateway(shortcutHelper),
            new FakeShortcutWriteAccessService(shortcutHelper),
            Mock.Of<IManagedShortcutLifecycleService>(),
            WindowsAppsAliasPathResolverFalse(),
            NonUwpKindService(),
            ProgramDataKnownPathResolver());

        service.EnforceBesideTargetShortcuts(
            [app],
            @"C:\RunFence\RunFence.Launcher.exe",
            _ => ("User", ""));

        Assert.Equal(0, shortcutHelper.WithShortcutCount);
    }

    [Fact]
    public void CreateBesideTargetShortcut_AppDataWindowsAppsAlias_DoesNotCreateShortcut()
    {
        var app = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            ExePath = Path.Combine(
                @"C:\Users\Target",
                "AppData",
                "Local",
                "Microsoft",
                "WindowsApps",
                "wt.exe")
        };
        var log = new Mock<ILoggingService>();
        var protection = new Mock<IShortcutProtectionService>();
        var shortcutHelper = new FakeShortcutComHelper(new FakeShortcut());
        var aliasResolver = new Mock<IWindowsAppsAliasPathResolver>();
        aliasResolver.Setup(s => s.IsWindowsAppsAliasPath(app.ExePath)).Returns(true);
        var service = new BesideTargetShortcutService(
            log.Object,
            protection.Object,
            new InMemoryShortcutProtectionStateStore(),
            new FakeShortcutGateway(shortcutHelper),
            new FakeShortcutWriteAccessService(shortcutHelper),
            Mock.Of<IManagedShortcutLifecycleService>(),
            aliasResolver.Object,
            NonUwpKindService(),
            ProgramDataKnownPathResolver());

        service.CreateBesideTargetShortcut(app, @"C:\RunFence\RunFence.Launcher.exe", "", "User");

        Assert.Equal(0, shortcutHelper.WithShortcutCount);
    }

    [Fact]
    public void EnforceBesideTargetShortcuts_AppDataWindowsAppsAlias_DoesNotCreateShortcut()
    {
        var app = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            Name = "Windows Terminal",
            ExePath = Path.Combine(
                @"C:\Users\Target",
                "AppData",
                "Local",
                "Microsoft",
                "WindowsApps",
                "wt.exe")
        };
        var log = new Mock<ILoggingService>();
        var protection = new Mock<IShortcutProtectionService>();
        var shortcutHelper = new FakeShortcutComHelper(new FakeShortcut());
        var aliasResolver = new Mock<IWindowsAppsAliasPathResolver>();
        aliasResolver.Setup(s => s.IsWindowsAppsAliasPath(app.ExePath)).Returns(true);
        var service = new BesideTargetShortcutService(
            log.Object,
            protection.Object,
            new InMemoryShortcutProtectionStateStore(),
            new FakeShortcutGateway(shortcutHelper),
            new FakeShortcutWriteAccessService(shortcutHelper),
            Mock.Of<IManagedShortcutLifecycleService>(),
            aliasResolver.Object,
            NonUwpKindService(),
            ProgramDataKnownPathResolver());

        service.EnforceBesideTargetShortcuts(
            [app],
            @"C:\RunFence\RunFence.Launcher.exe",
            _ => ("User", ""));

        Assert.Equal(0, shortcutHelper.WithShortcutCount);
    }

    [Fact]
    public void CreateBesideTargetShortcut_ProgramDataNonAcTarget_DoesNotCreateShortcut()
    {
        var targetDir = Path.Combine(
            PathConstants.ProgramDataDir,
            ProgramDataPolicies.Icons.RelativePath,
            $"ShortcutTarget_{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetDir);
        try
        {
            var app = new AppEntry
            {
                Id = AppEntry.GenerateId(),
                ExePath = Path.Combine(targetDir, "app.exe")
            };
            var shortcut = new FakeShortcut();
            var shortcutHelper = new FakeShortcutComHelper(shortcut);
            var service = CreateBesideTargetShortcutService(shortcutHelper);

            service.CreateBesideTargetShortcut(app, @"C:\RunFence\RunFence.Launcher.exe", "", "User");

            Assert.Equal(0, shortcut.SaveCount);
        }
        finally
        {
            Directory.Delete(targetDir, recursive: true);
        }
    }

    [Fact]
    public void EnforceBesideTargetShortcuts_ProgramDataNonAcTarget_DoesNotCreateShortcut()
    {
        var targetDir = Path.Combine(
            PathConstants.ProgramDataDir,
            ProgramDataPolicies.Temp.RelativePath,
            $"ShortcutTarget_{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetDir);
        try
        {
            var app = new AppEntry
            {
                Id = AppEntry.GenerateId(),
                Name = "Managed ProgramData App",
                ExePath = Path.Combine(targetDir, "app.exe")
            };
            var launcherPath = @"C:\RunFence\RunFence.Launcher.exe";
            var shortcut = new FakeShortcut
            {
                TargetPath = launcherPath,
                Arguments = app.Id,
                WorkingDirectory = Path.GetDirectoryName(launcherPath)!
            };
            File.WriteAllBytes(Path.Combine(targetDir, "app-as-User.lnk"), [0x4C, 0x00, 0x00, 0x00]);
            var shortcutHelper = new FakeShortcutComHelper(shortcut);
            var protection = new Mock<IShortcutProtectionService>();
            var service = CreateBesideTargetShortcutService(shortcutHelper, protection.Object);

            service.EnforceBesideTargetShortcuts(
                [app],
                launcherPath,
                _ => ("User", ""));

            Assert.Equal(0, shortcut.SaveCount);
            protection.Verify(p => p.ProtectShortcut(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>()), Times.Never);
        }
        finally
        {
            Directory.Delete(targetDir, recursive: true);
        }
    }

    [Fact]
    public void CreateBesideTargetShortcut_ProgramDataAcTarget_CreatesShortcut()
    {
        var targetDir = Path.Combine(
            PathConstants.ProgramDataDir,
            ProgramDataPolicies.Ac.RelativePath,
            $"ShortcutTarget_{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetDir);
        try
        {
            var app = new AppEntry
            {
                Id = AppEntry.GenerateId(),
                ExePath = Path.Combine(targetDir, "app.exe")
            };
            var shortcut = new FakeShortcut();
            var shortcutHelper = new FakeShortcutComHelper(shortcut);
            var protection = new Mock<IShortcutProtectionService>();
            var service = CreateBesideTargetShortcutService(shortcutHelper, protection.Object);
            var shortcutPath = Path.Combine(targetDir, "app-as-User.lnk");

            service.CreateBesideTargetShortcut(app, @"C:\RunFence\RunFence.Launcher.exe", "", "User");

            Assert.Equal(1, shortcut.SaveCount);
            protection.Verify(p => p.ProtectShortcut(
                app.Id,
                shortcutPath,
                true), Times.Once);
        }
        finally
        {
            Directory.Delete(targetDir, recursive: true);
        }
    }

    [Fact]
    public void EnforceBesideTargetShortcuts_ProgramDataAcTarget_CreatesShortcut()
    {
        var targetDir = Path.Combine(
            PathConstants.ProgramDataDir,
            ProgramDataPolicies.Ac.RelativePath,
            $"ShortcutTarget_{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetDir);
        try
        {
            var app = new AppEntry
            {
                Id = AppEntry.GenerateId(),
                Name = "AppContainer ProgramData App",
                ExePath = Path.Combine(targetDir, "app.exe")
            };
            var shortcut = new FakeShortcut();
            var shortcutHelper = new FakeShortcutComHelper(shortcut);
            var protection = new Mock<IShortcutProtectionService>();
            var service = CreateBesideTargetShortcutService(shortcutHelper, protection.Object);
            var shortcutPath = Path.Combine(targetDir, "app-as-User.lnk");

            service.EnforceBesideTargetShortcuts(
                [app],
                @"C:\RunFence\RunFence.Launcher.exe",
                _ => ("User", ""));

            Assert.Equal(1, shortcut.SaveCount);
            protection.Verify(p => p.ProtectShortcut(
                app.Id,
                shortcutPath,
                true), Times.Once);
        }
        finally
        {
            Directory.Delete(targetDir, recursive: true);
        }
    }

    [Fact]
    public void EnforceBesideTargetShortcuts_ManagedShortcutWithOldWorkingDirectory_RecreatesShortcut()
    {
        using var tempDir = new TempDirectory("BesideTargetShortcutService_Enforce");
        var appDir = Path.Combine(tempDir.Path, "App");
        Directory.CreateDirectory(appDir);
        var iconPath = Path.Combine(tempDir.Path, "app.ico");
        File.WriteAllBytes(iconPath, []);

        var app = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            Name = "Managed App",
            ExePath = Path.Combine(appDir, "app.exe"),
            ManageShortcuts = true
        };
        var shortcutPath = Path.Combine(appDir, "app-as-User.lnk");
        File.WriteAllBytes(shortcutPath, [0x4C, 0x00, 0x00, 0x00]);

        var launcherPath = Path.Combine(tempDir.Path, "Current", "RunFence.Launcher.exe");
        var shortcut = new FakeShortcut
        {
            TargetPath = launcherPath,
            Arguments = app.Id,
            WorkingDirectory = @"D:\OldRunFence",
            IconLocation = @"D:\OldIcons\old.ico,0"
        };
        var log = new Mock<ILoggingService>();
        var protection = new Mock<IShortcutProtectionService>();
        var shortcutHelper = new FakeShortcutComHelper(shortcut);
        var service = new BesideTargetShortcutService(
            log.Object,
            protection.Object,
            new InMemoryShortcutProtectionStateStore(),
            new FakeShortcutGateway(shortcutHelper),
            new FakeShortcutWriteAccessService(shortcutHelper),
            Mock.Of<IManagedShortcutLifecycleService>(),
            WindowsAppsAliasPathResolverFalse(),
            NonUwpKindService(),
            ProgramDataKnownPathResolver());

        service.EnforceBesideTargetShortcuts(
            [app],
            launcherPath,
            _ => ("User", iconPath));

        Assert.Equal(launcherPath, shortcut.TargetPath);
        Assert.Equal(Path.GetDirectoryName(launcherPath), shortcut.WorkingDirectory);
        Assert.Equal(app.Id, shortcut.Arguments);
        Assert.Equal($"{iconPath},0", shortcut.IconLocation);
        Assert.Equal(1, shortcut.SaveCount);
        Assert.Equal(1, shortcutHelper.WithShortcutCount);
        Assert.All(shortcutHelper.InvokedPaths, p => Assert.Equal(shortcutPath, p));
        protection.Verify(p => p.UnprotectShortcut(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        protection.Verify(p => p.ProtectShortcut(
            app.Id,
            shortcutPath,
            true), Times.Once);
    }

    [Fact]
    public void EnforceBesideTargetShortcuts_ManagedShortcutWithStaleIcon_RecreatesShortcut()
    {
        using var tempDir = new TempDirectory("BesideTargetShortcutService_Enforce_Icon");
        var appDir = Path.Combine(tempDir.Path, "App");
        Directory.CreateDirectory(appDir);
        var iconPath = Path.Combine(tempDir.Path, "app.ico");
        File.WriteAllBytes(iconPath, []);

        var app = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            Name = "Managed App",
            ExePath = Path.Combine(appDir, "app.exe"),
        };
        var shortcutPath = Path.Combine(appDir, "app-as-User.lnk");
        File.WriteAllBytes(shortcutPath, [0x4C, 0x00, 0x00, 0x00]);

        var launcherPath = Path.Combine(tempDir.Path, "Current", "RunFence.Launcher.exe");
        var shortcut = new FakeShortcut
        {
            TargetPath = launcherPath,
            Arguments = app.Id,
            WorkingDirectory = Path.GetDirectoryName(launcherPath)!,
            IconLocation = @"D:\OldIcons\old.ico,0"
        };
        var log = new Mock<ILoggingService>();
        var protection = new Mock<IShortcutProtectionService>();
        var shortcutHelper = new FakeShortcutComHelper(shortcut);
        var service = new BesideTargetShortcutService(
            log.Object,
            protection.Object,
            new InMemoryShortcutProtectionStateStore(),
            new FakeShortcutGateway(shortcutHelper),
            new FakeShortcutWriteAccessService(shortcutHelper),
            Mock.Of<IManagedShortcutLifecycleService>(),
            WindowsAppsAliasPathResolverFalse(),
            NonUwpKindService(),
            ProgramDataKnownPathResolver());

        service.EnforceBesideTargetShortcuts(
            [app],
            launcherPath,
            _ => ("User", iconPath));

        Assert.Equal(launcherPath, shortcut.TargetPath);
        Assert.Equal(Path.GetDirectoryName(launcherPath), shortcut.WorkingDirectory);
        Assert.Equal(app.Id, shortcut.Arguments);
        Assert.Equal($"{iconPath},0", shortcut.IconLocation);
        Assert.Equal(1, shortcut.SaveCount);
        Assert.Equal(1, shortcutHelper.WithShortcutCount);
        Assert.All(shortcutHelper.InvokedPaths, p => Assert.Equal(shortcutPath, p));
        protection.Verify(p => p.UnprotectShortcut(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        protection.Verify(p => p.ProtectShortcut(
            app.Id,
            shortcutPath,
            true), Times.Once);
    }

    [Fact]
    public void RemoveBesideTargetShortcut_MatchingManagedShortcut_UsesManagedLifecycleDelete()
    {
        using var tempDir = new TempDirectory("BesideTargetShortcutService_Remove");
        var appDir = Path.Combine(tempDir.Path, "App");
        Directory.CreateDirectory(appDir);
        var app = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            Name = "Managed App",
            ExePath = Path.Combine(appDir, "app.exe")
        };
        var shortcutPath = Path.Combine(appDir, "app-as-User.lnk");
        File.WriteAllBytes(shortcutPath, [0x4C, 0x00, 0x00, 0x00]);
        var shortcutHelper = new FakeShortcutComHelper(new FakeShortcut
        {
            TargetPath = Path.Combine(tempDir.Path, PathConstants.LauncherExeName),
            Arguments = app.Id
        });
        var lifecycle = new Mock<IManagedShortcutLifecycleService>();
        var service = new BesideTargetShortcutService(
            Mock.Of<ILoggingService>(),
            Mock.Of<IShortcutProtectionService>(),
            new InMemoryShortcutProtectionStateStore(),
            new FakeShortcutGateway(shortcutHelper),
            new FakeShortcutWriteAccessService(shortcutHelper),
            lifecycle.Object,
            WindowsAppsAliasPathResolverFalse(),
            NonUwpKindService(),
            ProgramDataKnownPathResolver());

        service.RemoveBesideTargetShortcut(app);

        lifecycle.Verify(l => l.DeleteManagedShortcutFile(shortcutPath), Times.Once);
    }

    [Fact]
    public void EnforceShortcuts_ManagedShortcutTargetingOldLauncher_UpdatesTargetAndWorkingDirectory()
    {
        var oldLauncherPath = @"D:\OldRunFence\RunFence.Launcher.exe";
        using var fx = CreateShortcutTestFixture("ShortcutService_Enforce",
            initialTarget: oldLauncherPath,
            initialWorkingDirectory: @"D:\OldRunFence");
        var launcherPath = Path.Combine(fx.TempDir.Path, "Current", "RunFence.Launcher.exe");
        var cache = new ShortcutTraversalCache(
            [new ShortcutTraversalEntry(fx.ShortcutPath, oldLauncherPath, fx.Shortcut.Arguments)]);

        fx.Service.EnforceShortcuts([fx.App], launcherPath, cache);

        Assert.Equal(1, fx.ShortcutHelper.WithShortcutCount);
        Assert.All(fx.ShortcutHelper.InvokedPaths, p => Assert.Equal(fx.ShortcutPath, p));
        Assert.Equal(launcherPath, fx.Shortcut.TargetPath);
        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(launcherPath)), fx.Shortcut.WorkingDirectory);
        Assert.Equal($"{fx.App.Id} --original", fx.Shortcut.Arguments);
        Assert.Equal(1, fx.Shortcut.SaveCount);
        var updatedEntry = Assert.Single(cache.Entries);
        Assert.Equal(launcherPath, updatedEntry.TargetPath);
        Assert.Equal($"{fx.App.Id} --original", updatedEntry.Arguments);
        fx.Protection.Verify(p => p.UnprotectShortcut(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        fx.Protection.Verify(p => p.ProtectShortcut(
            fx.App.Id,
            fx.ShortcutPath,
            false), Times.Once);
    }

    [Fact]
    public void EnforceShortcuts_ManagedShortcutAlreadyCorrectWithCustomWorkingDirectory_DoesNotRepair()
    {
        // Target already matches the current launcher — working dir is custom (not tracking the
        // launcher's parent dir), so no repair should happen: shortcut is just re-protected.
        using var fx = CreateShortcutTestFixture("ShortcutService_Enforce",
            initialWorkingDirectory: @"D:\CustomDir");
        var launcherPath = Path.Combine(fx.TempDir.Path, "Current", "RunFence.Launcher.exe");
        fx.Shortcut.TargetPath = launcherPath;
        var cache = new ShortcutTraversalCache(
            [new ShortcutTraversalEntry(fx.ShortcutPath, launcherPath, fx.Shortcut.Arguments)]);

        fx.Service.EnforceShortcuts([fx.App], launcherPath, cache);

        Assert.Equal(0, fx.Shortcut.SaveCount);
        Assert.Equal(0, fx.ShortcutHelper.WithShortcutCount);
        Assert.Equal(@"D:\CustomDir", fx.Shortcut.WorkingDirectory);
        var entry = Assert.Single(cache.Entries);
        Assert.Equal(launcherPath, entry.TargetPath);
        fx.Protection.Verify(p => p.UnprotectShortcut(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        fx.Protection.Verify(p => p.ProtectShortcut(
            fx.App.Id,
            fx.ShortcutPath,
            false), Times.Once);
    }

    [Fact]
    public void EnforceShortcuts_ManagedShortcutTargetingOldLauncher_WithCustomWorkingDirectory_UpdatesTargetOnly()
    {
        // Target points to old launcher, but working dir is custom (not the old launcher's parent).
        // Only the target should be updated; working dir must be left unchanged.
        var oldLauncherPath = @"D:\OldRunFence\RunFence.Launcher.exe";
        using var fx = CreateShortcutTestFixture("ShortcutService_Enforce",
            initialTarget: oldLauncherPath,
            initialWorkingDirectory: @"C:\CustomDir");
        var launcherPath = Path.Combine(fx.TempDir.Path, "Current", "RunFence.Launcher.exe");
        var cache = new ShortcutTraversalCache(
            [new ShortcutTraversalEntry(fx.ShortcutPath, oldLauncherPath, fx.Shortcut.Arguments)]);

        fx.Service.EnforceShortcuts([fx.App], launcherPath, cache);

        Assert.Equal(launcherPath, fx.Shortcut.TargetPath);
        Assert.Equal(@"C:\CustomDir", fx.Shortcut.WorkingDirectory);
        Assert.Equal($"{fx.App.Id} --original", fx.Shortcut.Arguments);
        Assert.Equal(1, fx.Shortcut.SaveCount);
        Assert.Equal(1, fx.ShortcutHelper.WithShortcutCount);
        Assert.All(fx.ShortcutHelper.InvokedPaths, p => Assert.Equal(fx.ShortcutPath, p));
        var updatedEntry = Assert.Single(cache.Entries);
        Assert.Equal(launcherPath, updatedEntry.TargetPath);
        fx.Protection.Verify(p => p.UnprotectShortcut(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        fx.Protection.Verify(p => p.ProtectShortcut(
            fx.App.Id,
            fx.ShortcutPath,
            false), Times.Once);
    }

    [Fact]
    public void EnforceShortcuts_ManagedShortcutRepairFails_ThrowsWarningException()
    {
        using var tempDir = new TempDirectory("ShortcutService_EnforceFailure");
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        File.WriteAllBytes(shortcutPath, [0x4C, 0x00, 0x00, 0x00]);
        var app = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            Name = "Managed App",
            ExePath = @"C:\Apps\Managed\app.exe",
            ManageShortcuts = true
        };
        var oldLauncherPath = @"D:\OldRunFence\RunFence.Launcher.exe";
        var shortcut = new FakeShortcut
        {
            TargetPath = oldLauncherPath,
            Arguments = $"{app.Id} --original",
            WorkingDirectory = @"D:\OldRunFence"
        };
        var shortcutHelper = new FakeShortcutComHelper(shortcut);
        var gateway = new FakeShortcutGateway(shortcutHelper);
        var protection = new Mock<IShortcutProtectionService>();
        var service = new ShortcutService(
            Mock.Of<ILoggingService>(),
            Mock.Of<IIconService>(),
            protection.Object,
            new InMemoryShortcutProtectionStateStore(),
            new ThrowingShortcutWriteAccessService(new UnauthorizedAccessException("restore privilege missing")),
            Mock.Of<IManagedShortcutLifecycleService>(),
            gateway,
            new Mock<IInteractiveUserDesktopProvider>().Object,
            new ShortcutFinder());
        var launcherPath = Path.Combine(tempDir.Path, "Current", "RunFence.Launcher.exe");
        var cache = new ShortcutTraversalCache(
        [
            new ShortcutTraversalEntry(shortcutPath, oldLauncherPath, shortcut.Arguments)
        ]);

        var ex = Assert.Throws<ShortcutEnforcementException>(() =>
            service.EnforceShortcuts([app], launcherPath, cache));

        Assert.Contains(shortcutPath, ex.Message);
        Assert.Contains("restore privilege missing", ex.Message);
        Assert.Single(ex.Causes);
        var appLevelException = Assert.IsType<ShortcutEnforcementException>(ex.Causes[0]);
        Assert.Single(appLevelException.Causes);
        Assert.IsType<UnauthorizedAccessException>(appLevelException.Causes[0]);
        Assert.IsType<ShortcutEnforcementException>(ex.InnerException);
        Assert.IsType<UnauthorizedAccessException>(appLevelException.InnerException);
        Assert.Equal(oldLauncherPath, shortcut.TargetPath);
        protection.Verify(p => p.ProtectShortcut(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void EnforceShortcuts_ManagedShortcutRepairWarning_ContinuesOtherAppsThenThrowsCombinedWarning()
    {
        using var tempDir = new TempDirectory("ShortcutService_EnforceWarningContinue");
        var firstShortcutPath = Path.Combine(tempDir.Path, "first.lnk");
        var secondShortcutPath = Path.Combine(tempDir.Path, "second.lnk");
        File.WriteAllBytes(firstShortcutPath, [0x4C, 0x00, 0x00, 0x00]);
        File.WriteAllBytes(secondShortcutPath, [0x4C, 0x00, 0x00, 0x00]);

        var firstApp = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            Name = "First App",
            ExePath = @"C:\Apps\First\app.exe",
            ManageShortcuts = true
        };
        var secondApp = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            Name = "Second App",
            ExePath = @"C:\Apps\Second\app.exe",
            ManageShortcuts = true
        };
        var firstOldLauncher = @"D:\OldFirst\RunFence.Launcher.exe";
        var secondOldLauncher = @"D:\OldSecond\RunFence.Launcher.exe";
        var firstShortcut = new FakeShortcut
        {
            TargetPath = firstOldLauncher,
            Arguments = $"{firstApp.Id} --first",
            WorkingDirectory = @"D:\OldFirst"
        };
        var secondShortcut = new FakeShortcut
        {
            TargetPath = secondOldLauncher,
            Arguments = $"{secondApp.Id} --second",
            WorkingDirectory = @"D:\OldSecond"
        };
        var shortcutHelper = new MultiShortcutComHelper(new Dictionary<string, FakeShortcut>(StringComparer.OrdinalIgnoreCase)
        {
            [firstShortcutPath] = firstShortcut,
            [secondShortcutPath] = secondShortcut
        });
        var gateway = new FakeShortcutGateway(shortcutHelper);
        var protection = new Mock<IShortcutProtectionService>();
        var service = new ShortcutService(
            Mock.Of<ILoggingService>(),
            Mock.Of<IIconService>(),
            protection.Object,
            new InMemoryShortcutProtectionStateStore(),
            new SelectiveThrowingShortcutWriteAccessService(firstShortcutPath, shortcutHelper),
            Mock.Of<IManagedShortcutLifecycleService>(),
            gateway,
            new Mock<IInteractiveUserDesktopProvider>().Object,
            new ShortcutFinder());
        var launcherPath = Path.Combine(tempDir.Path, "Current", "RunFence.Launcher.exe");
        var cache = new ShortcutTraversalCache(
        [
            new ShortcutTraversalEntry(firstShortcutPath, firstOldLauncher, firstShortcut.Arguments),
            new ShortcutTraversalEntry(secondShortcutPath, secondOldLauncher, secondShortcut.Arguments)
        ]);

        var ex = Assert.Throws<ShortcutEnforcementException>(() =>
            service.EnforceShortcuts([firstApp, secondApp], launcherPath, cache));

        Assert.Contains(firstShortcutPath, ex.Message);
        Assert.DoesNotContain(secondShortcutPath, ex.Message);
        Assert.Equal(firstOldLauncher, firstShortcut.TargetPath);
        Assert.Equal(launcherPath, secondShortcut.TargetPath);
        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(launcherPath)), secondShortcut.WorkingDirectory);
        protection.Verify(p => p.ProtectShortcut(
            secondApp.Id,
            secondShortcutPath,
            false), Times.Once);
    }

    [Fact]
    public void ReplaceShortcuts_ManagedShortcutTargetingOldLauncher_UpdatesTargetAndWorkingDirectory()
    {
        var oldLauncherPath = @"D:\OldRunFence\RunFence.Launcher.exe";
        using var fx = CreateShortcutTestFixture("ShortcutService_Replace",
            initialTarget: oldLauncherPath,
            initialWorkingDirectory: @"D:\OldRunFence");
        var launcherPath = Path.Combine(fx.TempDir.Path, "Current", "RunFence.Launcher.exe");
        var cache = new ShortcutTraversalCache(
            [new ShortcutTraversalEntry(fx.ShortcutPath, oldLauncherPath, fx.Shortcut.Arguments)]);

        fx.Service.ReplaceShortcuts(fx.App, launcherPath, iconPath: "", cache);

        Assert.Equal(launcherPath, fx.Shortcut.TargetPath);
        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(launcherPath)), fx.Shortcut.WorkingDirectory);
        Assert.Equal($"{fx.App.Id} --original", fx.Shortcut.Arguments);
        Assert.Equal(1, fx.Shortcut.SaveCount);
        Assert.All(fx.ShortcutHelper.InvokedPaths, p => Assert.Equal(fx.ShortcutPath, p));
        var updatedEntry = Assert.Single(cache.Entries);
        Assert.Equal(launcherPath, updatedEntry.TargetPath);
        Assert.Equal($"{fx.App.Id} --original", updatedEntry.Arguments);
        fx.Protection.Verify(p => p.UnprotectShortcut(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        fx.Protection.Verify(p => p.ProtectShortcut(
            fx.App.Id,
            fx.ShortcutPath,
            false), Times.Once);
    }

    [Fact]
    public void ShortcutTraversalCache_RecordAndRemove_MaintainsCaseInsensitiveIndexAndOrder()
    {
        var cache = new ShortcutTraversalCache(
        [
            new ShortcutTraversalEntry(@"C:\Links\One.lnk", @"C:\One.exe", null),
            new ShortcutTraversalEntry(@"C:\Links\Two.lnk", @"C:\Two.exe", null)
        ]);

        cache.RecordShortcut(@"c:\links\one.lnk", @"C:\Updated.exe", "--updated");
        cache.RecordShortcut(@"C:\Links\Three.lnk", @"C:\Three.exe", null);
        cache.RemoveShortcut(@"c:\links\two.lnk");
        cache.RecordShortcut(@"C:\Links\Four.lnk", @"C:\Four.exe", null);

        var entries = cache.Entries;
        Assert.Equal(3, entries.Count);
        Assert.Equal(@"c:\links\one.lnk", entries[0].Path);
        Assert.Equal(@"C:\Updated.exe", entries[0].TargetPath);
        Assert.Equal("--updated", entries[0].Arguments);
        Assert.Equal(@"C:\Links\Three.lnk", entries[1].Path);
        Assert.Equal(@"C:\Links\Four.lnk", entries[2].Path);
    }

    [Fact]
    public void ReplaceShortcuts_SaveSucceedsButProtectFails_ThrowsAndUpdatesCache()
    {
        using var tempDir = new TempDirectory("ShortcutService_ProtectFailure");
        var shortcutPath = Path.Combine(tempDir.Path, "app.lnk");
        File.WriteAllBytes(shortcutPath, [0x4C, 0x00, 0x00, 0x00]);

        var app = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            Name = "App",
            ExePath = @"C:\Apps\App.exe",
            ManageShortcuts = true
        };
        var launcherPath = Path.Combine(tempDir.Path, "RunFence.Launcher.exe");
        var shortcut = new FakeShortcut
        {
            TargetPath = app.ExePath,
            Arguments = "--original",
            WorkingDirectory = @"C:\Apps"
        };
        var cache = new ShortcutTraversalCache(
            [new ShortcutTraversalEntry(shortcutPath, app.ExePath, shortcut.Arguments)]);

        var log = new Mock<ILoggingService>();
        var protection = new Mock<IShortcutProtectionService>();
        protection.SetupSequence(p => p.ProtectShortcut(
                app.Id,
                shortcutPath,
                false))
            .Throws(new IOException("protect failed"))
            .Pass();
        var shortcutHelper = new FakeShortcutComHelper(shortcut);
        var gateway = new FakeShortcutGateway(shortcutHelper);
        var service = new ShortcutService(
            log.Object,
            Mock.Of<IIconService>(),
            protection.Object,
            new InMemoryShortcutProtectionStateStore(),
            new FakeShortcutWriteAccessService(shortcutHelper),
            Mock.Of<IManagedShortcutLifecycleService>(),
            gateway,
            new Mock<IInteractiveUserDesktopProvider>().Object,
            new ShortcutFinder());

        var ex = Assert.Throws<IOException>(() => service.ReplaceShortcuts(app, launcherPath, iconPath: "", cache));

        var entry = Assert.Single(cache.Entries);
        Assert.Equal(launcherPath, entry.TargetPath);
        Assert.Equal($"{app.Id} --original", entry.Arguments);
        Assert.Equal(1, shortcut.SaveCount);
        Assert.All(shortcutHelper.InvokedPaths, p => Assert.Equal(shortcutPath, p));
        Assert.Equal("protect failed", ex.Message);
    }

    [Fact]
    public void RevertShortcuts_ShortcutProtectionExceptionFromPersistence_IsNotSwallowed()
    {
        var app = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            ExePath = @"C:\Apps\Managed\app.exe"
        };
        var shortcutPath = @"C:\Shortcuts\Managed.lnk";
        var shortcut = new FakeShortcut
        {
            TargetPath = Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName),
            Arguments = $"{app.Id} --original"
        };
        var lifecycle = new Mock<IManagedShortcutLifecycleService>();
        lifecycle.Setup(w => w.RewriteManagedShortcutFile(shortcutPath, It.IsAny<ShortcutMutation>(), It.IsAny<ShortcutDestinationMetadataMode>(), It.IsAny<ShortcutContentMode>()))
            .Throws(new ShortcutProtectionException(shortcutPath, "replace", new IOException("deny")));
        var gateway = new FakeShortcutGateway(new FakeShortcutComHelper(shortcut));

        var service = new ShortcutService(
            Mock.Of<ILoggingService>(),
            Mock.Of<IIconService>(),
            Mock.Of<IShortcutProtectionService>(),
            new InMemoryShortcutProtectionStateStore(),
            Mock.Of<IShortcutWriteAccessService>(),
            lifecycle.Object,
            gateway,
            new Mock<IInteractiveUserDesktopProvider>().Object,
            new ShortcutFinder());
        var cache = new ShortcutTraversalCache(
            [new ShortcutTraversalEntry(shortcutPath, shortcut.TargetPath, shortcut.Arguments)]);

        Assert.Throws<ShortcutProtectionException>(() => service.RevertShortcuts(app, cache));
    }

    [Fact]
    public void SaveShortcut_DelegatesFieldUpdatesThroughWriteAccessService()
    {
        var app = new AppEntry { Id = AppEntry.GenerateId() };
        var shortcutPath = @"C:\Shortcuts\App.lnk";
        var launcherPath = Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName);
        var iconPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.ico");
        File.WriteAllBytes(iconPath, []);
        ShortcutMutation? savedMutation = null;
        var writer = new Mock<IShortcutWriteAccessService>();
        writer.Setup(w => w.Save(shortcutPath, It.IsAny<ShortcutMutation>(), It.IsAny<ShortcutDestinationMetadataMode>(), It.IsAny<ShortcutContentMode>()))
            .Callback<string, ShortcutMutation, ShortcutDestinationMetadataMode, ShortcutContentMode>((_, mutation, _, _) => savedMutation = mutation);

        try
        {
            var service = new ShortcutService(
                Mock.Of<ILoggingService>(),
                Mock.Of<IIconService>(i => i.GetIconPath(app.Id) == iconPath),
                Mock.Of<IShortcutProtectionService>(),
                new InMemoryShortcutProtectionStateStore(),
                writer.Object,
                Mock.Of<IManagedShortcutLifecycleService>(),
                Mock.Of<IShortcutGateway>(),
                new Mock<IInteractiveUserDesktopProvider>().Object,
                new ShortcutFinder());

            service.SaveShortcut(app, shortcutPath);

            writer.Verify(w => w.Save(shortcutPath, It.IsAny<ShortcutMutation>(), ShortcutDestinationMetadataMode.PreserveExisting, ShortcutContentMode.RecreateCanonical), Times.Once);
            Assert.NotNull(savedMutation);
            Assert.Equal(launcherPath, savedMutation!.TargetPath);
            Assert.Equal(app.Id, savedMutation.Arguments);
            Assert.Equal(Path.GetDirectoryName(launcherPath), savedMutation.WorkingDirectory);
            Assert.Equal($"{iconPath},0", savedMutation.IconLocation);
            Assert.Equal(ShortcutIconUpdateMode.Set, savedMutation.IconUpdateMode);
        }
        finally
        {
            File.Delete(iconPath);
        }
    }

    [Fact]
    public void SaveShortcut_NewManagedShortcut_ProtectsCreatedShortcut()
    {
        using var tempDir = new TempDirectory("ShortcutService_SaveShortcut_Protect");
        var app = new AppEntry { Id = AppEntry.GenerateId() };
        var shortcutPath = Path.Combine(tempDir.Path, "App.lnk");
        var launcherPath = Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName);
        var writer = new Mock<IShortcutWriteAccessService>();
        var protection = new Mock<IShortcutProtectionService>();
        writer.Setup(w => w.Save(shortcutPath, It.IsAny<ShortcutMutation>(), It.IsAny<ShortcutDestinationMetadataMode>(), It.IsAny<ShortcutContentMode>()))
            .Callback(() => File.WriteAllBytes(shortcutPath, [0x4C, 0x00, 0x00, 0x00]));

        var service = new ShortcutService(
            Mock.Of<ILoggingService>(),
            Mock.Of<IIconService>(),
            protection.Object,
            new InMemoryShortcutProtectionStateStore(),
            writer.Object,
            Mock.Of<IManagedShortcutLifecycleService>(),
            Mock.Of<IShortcutGateway>(),
            new Mock<IInteractiveUserDesktopProvider>().Object,
            new ShortcutFinder());

        service.SaveShortcut(app, shortcutPath);

        protection.Verify(p => p.ProtectShortcut(
            app.Id,
            shortcutPath,
            false), Times.Once);
    }

    [Fact]
    public void RevertSingleShortcut_ClearingIconFailure_IsIgnored()
    {
        var app = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            ExePath = @"C:\Apps\Managed\app.exe"
        };
        var shortcutPath = @"C:\Shortcuts\Managed.lnk";
        var shortcut = new FakeShortcut
        {
            TargetPath = Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName),
            Arguments = $"{app.Id} --original"
        };
        ShortcutMutation? savedMutation = null;
        ShortcutContentMode? savedContentMode = null;
        ShortcutDestinationMetadataMode? savedMetadataMode = null;
        var lifecycle = new Mock<IManagedShortcutLifecycleService>();
        lifecycle.Setup(w => w.RewriteManagedShortcutFile(shortcutPath, It.IsAny<ShortcutMutation>(), It.IsAny<ShortcutDestinationMetadataMode>(), It.IsAny<ShortcutContentMode>()))
            .Callback<string, ShortcutMutation, ShortcutDestinationMetadataMode, ShortcutContentMode>((_, mutation, metadataMode, contentMode) =>
            {
                savedMutation = mutation;
                savedMetadataMode = metadataMode;
                savedContentMode = contentMode;
            });

        var service = new ShortcutService(
            Mock.Of<ILoggingService>(),
            Mock.Of<IIconService>(),
            Mock.Of<IShortcutProtectionService>(),
            new InMemoryShortcutProtectionStateStore(),
            Mock.Of<IShortcutWriteAccessService>(),
            lifecycle.Object,
            new FakeShortcutGateway(new FakeShortcutComHelper(shortcut)),
            new Mock<IInteractiveUserDesktopProvider>().Object,
            new ShortcutFinder());

        var reverted = service.RevertSingleShortcut(shortcutPath, app);

        Assert.True(reverted);
        Assert.NotNull(savedMutation);
        Assert.Equal(app.ExePath, savedMutation!.TargetPath);
        Assert.Equal("--original", savedMutation.Arguments);
        Assert.Equal(Path.GetDirectoryName(app.ExePath), savedMutation.WorkingDirectory);
        Assert.Equal(ShortcutIconUpdateMode.ClearBestEffort, savedMutation.IconUpdateMode);
        Assert.Equal(ShortcutDestinationMetadataMode.ResetForRecreatedShortcut, savedMetadataMode);
        Assert.Equal(ShortcutContentMode.PreserveExisting, savedContentMode);
    }

    public sealed class FakeShortcut
    {
        public string TargetPath { get; set; } = "";
        public string Arguments { get; set; } = "";
        public string WorkingDirectory { get; set; } = "";
        public string? IconLocation { get; set; }
        public string? Description { get; set; }
        public string? Hotkey { get; set; }
        public int WindowStyle { get; set; } = 1;
        public int SaveCount { get; private set; }
        public Queue<Exception?> SaveExceptionSequence { get; } = [];

        public void Save()
        {
            SaveCount++;
            if (SaveExceptionSequence.Count > 0)
            {
                var next = SaveExceptionSequence.Dequeue();
                if (next != null)
                    throw next;
            }
        }
    }

    private sealed class FakeShortcutComHelper : IShortcutComHelper
    {
        private readonly List<string> _invokedPaths = [];
        private readonly FakeShortcut _shortcut;

        public FakeShortcutComHelper(FakeShortcut shortcut)
        {
            _shortcut = shortcut;
        }

        public FakeShortcut Shortcut => _shortcut;
        public int WithShortcutCount => _invokedPaths.Count;
        public IReadOnlyList<string> InvokedPaths => _invokedPaths;

        public T WithShortcut<T>(string path, Func<dynamic, T> action)
        {
            _invokedPaths.Add(path);
            return action((dynamic)_shortcut);
        }

        public void WithShortcut(string path, Action<dynamic> action)
        {
            _invokedPaths.Add(path);
            action((dynamic)_shortcut);
        }

        public ShortcutDefinition GetShortcutDefinition(string path)
            => new(path, _shortcut.TargetPath, _shortcut.Arguments, _shortcut.WorkingDirectory);

    }

    private sealed class FakeShortcutWriteAccessService(FakeShortcutComHelper shortcutHelper)
        : IShortcutWriteAccessService
    {
        public void Save(
            string shortcutPath,
            ShortcutMutation mutation,
            ShortcutDestinationMetadataMode metadataMode,
            ShortcutContentMode contentMode)
        {
            shortcutHelper.Shortcut.TargetPath = mutation.TargetPath;
            shortcutHelper.Shortcut.Arguments = mutation.Arguments ?? "";
            shortcutHelper.Shortcut.WorkingDirectory = mutation.WorkingDirectory ?? "";
            shortcutHelper.Shortcut.Description = mutation.Description;
            shortcutHelper.Shortcut.Hotkey = mutation.Hotkey;
            shortcutHelper.Shortcut.WindowStyle = mutation.WindowStyle;
            if (mutation.IconUpdateMode == ShortcutIconUpdateMode.Set)
                shortcutHelper.Shortcut.IconLocation = mutation.IconLocation;
            else if (mutation.IconUpdateMode == ShortcutIconUpdateMode.ClearBestEffort)
                shortcutHelper.Shortcut.IconLocation = "";
            shortcutHelper.Shortcut.Save();
        }
    }

    private sealed class FakeShortcutGateway(IShortcutComHelper shortcutHelper) : IShortcutGateway
    {
        public ShortcutData Read(string shortcutPath)
        {
            return shortcutHelper.WithShortcut(shortcutPath, shortcut =>
            {
                var definition = shortcutHelper.GetShortcutDefinition(shortcutPath);
                return new ShortcutData(
                    definition.TargetPath ?? string.Empty,
                    definition.Arguments,
                    definition.WorkingDirectory,
                    ((string?)shortcut.IconLocation)?.Split(',')[0],
                    0,
                    shortcut.Description,
                    0,
                    (int?)shortcut.WindowStyle ?? 1);
            });
        }

        public ShortcutMutation ReadMutationState(string shortcutPath)
            => shortcutHelper.WithShortcut(shortcutPath, shortcut =>
            {
                var definition = shortcutHelper.GetShortcutDefinition(shortcutPath);
                return new ShortcutMutation(
                    definition.TargetPath ?? string.Empty,
                    definition.Arguments,
                    definition.WorkingDirectory,
                    (string?)shortcut.IconLocation,
                    ShortcutIconUpdateMode.None,
                    shortcut.Description,
                    shortcut.Hotkey?.ToString(),
                    (int?)shortcut.WindowStyle ?? 1);
            });

        public void Write(string shortcutPath, ShortcutData data)
            => throw new NotSupportedException();

        public void WriteMutationState(string shortcutPath, ShortcutMutation mutation)
            => throw new NotSupportedException();

        public void Delete(string shortcutPath)
            => shortcutHelper.WithShortcut(shortcutPath, shortcut => shortcut.TargetPath = string.Empty);
    }

    private sealed class ThrowingShortcutWriteAccessService(Exception exception)
        : IShortcutWriteAccessService
    {
        public void Save(
            string shortcutPath,
            ShortcutMutation mutation,
            ShortcutDestinationMetadataMode metadataMode,
            ShortcutContentMode contentMode) => throw exception;
    }

    private sealed class SelectiveThrowingShortcutWriteAccessService(
        string failingShortcutPath,
        MultiShortcutComHelper shortcutHelper) : IShortcutWriteAccessService
    {
        public void Save(
            string shortcutPath,
            ShortcutMutation mutation,
            ShortcutDestinationMetadataMode metadataMode,
            ShortcutContentMode contentMode)
        {
            if (string.Equals(shortcutPath, failingShortcutPath, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("restore privilege missing");

            var shortcut = shortcutHelper.GetShortcut(shortcutPath);
            shortcut.TargetPath = mutation.TargetPath;
            shortcut.Arguments = mutation.Arguments ?? "";
            shortcut.WorkingDirectory = mutation.WorkingDirectory ?? "";
            shortcut.Description = mutation.Description;
            shortcut.Hotkey = mutation.Hotkey;
            shortcut.WindowStyle = mutation.WindowStyle;
            if (mutation.IconUpdateMode == ShortcutIconUpdateMode.Set)
                shortcut.IconLocation = mutation.IconLocation;
            else if (mutation.IconUpdateMode == ShortcutIconUpdateMode.ClearBestEffort)
                shortcut.IconLocation = "";
            shortcut.Save();
        }
    }

    private sealed class MultiShortcutComHelper(Dictionary<string, FakeShortcut> shortcuts) : IShortcutComHelper
    {
        public FakeShortcut GetShortcut(string path) => shortcuts[path];

        public T WithShortcut<T>(string path, Func<dynamic, T> action)
            => action(shortcuts[path]);

        public void WithShortcut(string path, Action<dynamic> action)
            => action(shortcuts[path]);

        public ShortcutDefinition GetShortcutDefinition(string path)
        {
            var shortcut = shortcuts[path];
            return new ShortcutDefinition(path, shortcut.TargetPath, shortcut.Arguments, shortcut.WorkingDirectory);
        }
    }

    private sealed record ShortcutTestFixture(
        TempDirectory TempDir,
        string ShortcutPath,
        AppEntry App,
        FakeShortcut Shortcut,
        FakeShortcutComHelper ShortcutHelper,
        Mock<IShortcutProtectionService> Protection,
        ShortcutService Service) : IDisposable
    {
        public void Dispose() => TempDir.Dispose();
    }

    private static ShortcutTestFixture CreateShortcutTestFixture(
        string prefix,
        string? initialTarget = null,
        string? initialWorkingDirectory = null,
        string? appArgs = null)
    {
        var tempDir = new TempDirectory(prefix);
        var shortcutPath = Path.Combine(tempDir.Path, "managed.lnk");
        File.WriteAllBytes(shortcutPath, [0x4C, 0x00, 0x00, 0x00]);

        var app = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            Name = "Managed App",
            ExePath = @"C:\Apps\Managed\app.exe",
            ManageShortcuts = true
        };

        var shortcut = new FakeShortcut
        {
            TargetPath = initialTarget ?? "",
            Arguments = appArgs ?? $"{app.Id} --original",
            WorkingDirectory = initialWorkingDirectory ?? ""
        };

        var log = new Mock<ILoggingService>();
        var protection = new Mock<IShortcutProtectionService>();
        var shortcutHelper = new FakeShortcutComHelper(shortcut);
        var gateway = new FakeShortcutGateway(shortcutHelper);
        var service = new ShortcutService(
            log.Object,
            Mock.Of<IIconService>(),
            protection.Object,
            new InMemoryShortcutProtectionStateStore(),
            new FakeShortcutWriteAccessService(shortcutHelper),
            Mock.Of<IManagedShortcutLifecycleService>(),
            gateway,
            new Mock<IInteractiveUserDesktopProvider>().Object,
            new ShortcutFinder());

        return new ShortcutTestFixture(tempDir, shortcutPath, app, shortcut, shortcutHelper,
            protection, service);
    }

    // --- ProtectInternalShortcut tests ---
    // These tests assume the test runner is a non-admin user.
    // As a non-admin, after ProtectInternalShortcut applies the strict ACL (Admins + LocalSystem +
    // accountSid=ReadAndExecute), the caller loses write access, which is the documented behavior.
    // Running as admin bypasses this constraint — the tests are still valid, but the ReadOnly
    // assertion will differ because admins can always set file attributes.
    // CLAUDE.md: "Tests run under normal non-admin non-elevated user only."

    private static (string shortcutPath, TempDirectory tempDir) CreateTempShortcut()
    {
        var tempDir = new TempDirectory("ShortcutProtection_Test");
        var path = Path.Combine(tempDir.Path, "test.lnk");
        File.WriteAllBytes(path, [0x4C, 0x00, 0x00, 0x00]); // minimal .lnk header bytes
        return (path, tempDir);
    }

    private static void RestoreFileForCleanup(string path)
    {
        // Re-grant FullControl to the current user and remove ReadOnly so TempDirectory can clean up.
        try
        {
            var fileInfo = new FileInfo(path);
            var security = fileInfo.GetAccessControl();
            var currentUser = WindowsIdentity.GetCurrent().User!;
            security.SetAccessRuleProtection(false, false);
            security.AddAccessRule(new FileSystemAccessRule(
                currentUser, FileSystemRights.FullControl, AccessControlType.Allow));
            fileInfo.SetAccessControl(security);
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }
        catch
        {
        } // best-effort
    }

    private static bool HasAce(string path, string sid, FileSystemRights rights, AccessControlType type)
    {
        var rules = new FileInfo(path)
            .GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.IdentityReference is SecurityIdentifier s && s.Value == sid &&
                rule.AccessControlType == type &&
                (rule.FileSystemRights & rights) == rights)
                return true;
        }

        return false;
    }

    [Fact]
    public void ProtectInternalShortcut_SetsCorrectAcl()
    {
        var (path, tempDir) = CreateTempShortcut();
        try
        {
            var log = new Mock<ILoggingService>();
            var service = ShortcutProtectionTestFactory.Create(log.Object, AclAccessorFactory.Create(),
                CreateStateStore());
            var accountSid = WindowsIdentity.GetCurrent().User!.Value;

            service.ProtectInternalShortcut("app1", path, accountSid);

            // ACL should have explicit Admins=FullControl, accountSid=ReadAndExecute, LocalSystem=FullControl
            var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value;
            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;

            Assert.True(HasAce(path, adminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
                "Administrators should have FullControl");
            Assert.True(HasAce(path, systemSid, FileSystemRights.FullControl, AccessControlType.Allow),
                "LocalSystem should have FullControl");
            Assert.True(HasAce(path, accountSid, FileSystemRights.ReadAndExecute, AccessControlType.Allow),
                "Account SID should have ReadAndExecute");
        }
        finally
        {
            RestoreFileForCleanup(path);
            tempDir.Dispose();
        }
    }

    [Fact]
    public void ProtectInternalShortcut_SetsReadOnlyBeforeStrictAcl()
    {
        var (path, tempDir) = CreateTempShortcut();
        try
        {
            var log = new Mock<ILoggingService>();
            var service = ShortcutProtectionTestFactory.Create(log.Object, AclAccessorFactory.Create(),
                CreateStateStore());
            var accountSid = WindowsIdentity.GetCurrent().User!.Value;

            service.ProtectInternalShortcut("app1", path, accountSid);

            Assert.True((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0,
                "File should become read-only in unit-test admin-operation mock mode because the current user keeps write access.");
        }
        finally
        {
            RestoreFileForCleanup(path);
            tempDir.Dispose();
        }
    }

    [Fact]
    public void ProtectInternalShortcut_InheritanceBroken()
    {
        var (path, tempDir) = CreateTempShortcut();
        try
        {
            var log = new Mock<ILoggingService>();
            var service = ShortcutProtectionTestFactory.Create(log.Object, AclAccessorFactory.Create(),
                CreateStateStore());
            var accountSid = WindowsIdentity.GetCurrent().User!.Value;

            service.ProtectInternalShortcut("app1", path, accountSid);

            var security = new FileInfo(path).GetAccessControl();
            Assert.True(security.AreAccessRulesProtected,
                "Inheritance should be broken (rules protected) after ProtectInternalShortcut");
        }
        finally
        {
            RestoreFileForCleanup(path);
            tempDir.Dispose();
        }
    }

    [Fact]
    public void ProtectInternalShortcut_Idempotent_AclUnchangedOnSecondCall()
    {
        var (path, tempDir) = CreateTempShortcut();
        try
        {
            var log = new Mock<ILoggingService>();
            var service = ShortcutProtectionTestFactory.Create(log.Object, AclAccessorFactory.Create(),
                CreateStateStore());
            var accountSid = WindowsIdentity.GetCurrent().User!.Value;

            service.ProtectInternalShortcut("app1", path, accountSid);

            // Record ACL after first call
            var rulesAfterFirst = new FileInfo(path)
                .GetAccessControl()
                .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Select(r => $"{r.IdentityReference.Value}:{r.FileSystemRights}:{r.AccessControlType}")
                .OrderBy(x => x)
                .ToList();

            // Reset log to track second call errors separately
            var log2 = new Mock<ILoggingService>();
            service = ShortcutProtectionTestFactory.Create(log2.Object, AclAccessorFactory.Create(),
                CreateStateStore());
            service.ProtectInternalShortcut("app1", path, accountSid); // second call on already-protected file

            var rulesAfterSecond = new FileInfo(path)
                .GetAccessControl()
                .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Select(r => $"{r.IdentityReference.Value}:{r.FileSystemRights}:{r.AccessControlType}")
                .OrderBy(x => x)
                .ToList();

            Assert.Equal(rulesAfterFirst, rulesAfterSecond);
        }
        finally
        {
            RestoreFileForCleanup(path);
            tempDir.Dispose();
        }
    }

    private static BesideTargetShortcutService CreateBesideTargetShortcutService(
        FakeShortcutComHelper shortcutHelper,
        IShortcutProtectionService? protection = null)
        => new(
            Mock.Of<ILoggingService>(),
            protection ?? Mock.Of<IShortcutProtectionService>(),
            new InMemoryShortcutProtectionStateStore(),
            new FakeShortcutGateway(shortcutHelper),
            new FakeShortcutWriteAccessService(shortcutHelper),
            Mock.Of<IManagedShortcutLifecycleService>(),
            WindowsAppsAliasPathResolverFalse(),
            NonUwpKindService(),
            ProgramDataKnownPathResolver());

    private static IProgramDataKnownPathResolver ProgramDataKnownPathResolver()
    {
        var resolver = new Mock<IProgramDataKnownPathResolver>();
        resolver.Setup(r => r.GetDirectoryPath(ProgramDataPolicies.Ac))
            .Returns(Path.Combine(PathConstants.ProgramDataDir, ProgramDataPolicies.Ac.RelativePath));
        return resolver.Object;
    }

    private static IExecutableKindService NonUwpKindService()
    {
        var service = new Mock<IExecutableKindService>();
        service.Setup(s => s.IsUwpExeFile(It.IsAny<string>())).Returns(false);
        service.Setup(s => s.IsKnownBrowserExe(It.IsAny<string>())).Returns(false);
        service.Setup(s => s.SuggestsBasicPrivilegeLevel(It.IsAny<string>())).Returns(false);
        return service.Object;
    }

    private static IWindowsAppsAliasPathResolver WindowsAppsAliasPathResolverFalse()
    {
        var resolver = new Mock<IWindowsAppsAliasPathResolver>();
        resolver.Setup(s => s.IsWindowsAppsAliasPath(It.IsAny<string>())).Returns(false);
        return resolver.Object;
    }

    private static IExecutableKindService UwpKindService()
    {
        var service = new Mock<IExecutableKindService>();
        service.Setup(s => s.IsUwpExeFile(It.IsAny<string>())).Returns(true);
        service.Setup(s => s.IsKnownBrowserExe(It.IsAny<string>())).Returns(false);
        service.Setup(s => s.SuggestsBasicPrivilegeLevel(It.IsAny<string>())).Returns(true);
        return service.Object;
    }

    private static IShortcutProtectionStateStore CreateStateStore()
        => new InMemoryShortcutProtectionStateStore();
}

