using Moq;
using RunFence.Acl;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launching.Resolution;
using Xunit;

namespace RunFence.Tests;

public class ShortcutComGatewayTests
{
    [Theory]
    [InlineData(@"C:\Tools, Inc\app.ico", @"C:\Tools, Inc\app.ico", 0)]
    [InlineData(@"C:\Tools, Inc\app.ico,0", @"C:\Tools, Inc\app.ico", 0)]
    [InlineData(@"C:\Tools\app.ico,2", @"C:\Tools\app.ico", 2)]
    public void Read_ParsesIconLocationAsExpected(string iconLocation, string expectedPath, int expectedIndex)
    {
        var helper = new FakeShortcutComHelper(new FakeShortcut { IconLocation = iconLocation });
        var gateway = new ShortcutComGateway(helper, Mock.Of<IShortcutFilePersistenceNative>());

        var result = gateway.Read(@"C:\Shortcuts\App.lnk");

        Assert.Equal(expectedPath, result.IconPath);
        Assert.Equal(expectedIndex, result.IconIndex);
    }

    [Fact]
    public void EnforceBesideTargetShortcuts_CommaContainingIconPath_DoesNotRecreateUpToDateShortcut()
    {
        using var tempDir = new TempDirectory("ShortcutComGateway_CommaIcon");
        var appDir = Path.Combine(tempDir.Path, "App");
        Directory.CreateDirectory(appDir);
        var iconDir = Path.Combine(tempDir.Path, "Tools, Inc");
        Directory.CreateDirectory(iconDir);
        var iconPath = Path.Combine(iconDir, "app.ico");
        File.WriteAllBytes(iconPath, []);

        var launcherDir = Path.Combine(tempDir.Path, "Current");
        Directory.CreateDirectory(launcherDir);
        var launcherPath = Path.Combine(launcherDir, "RunFence.Launcher.exe");
        var shortcutPath = Path.Combine(appDir, "app-as-User.lnk");
        File.WriteAllBytes(shortcutPath, [0x4C, 0x00, 0x00, 0x00]);

        var app = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            Name = "Managed App",
            ExePath = Path.Combine(appDir, "app.exe"),
            ManageShortcuts = true
        };

        var shortcut = new FakeShortcut
        {
            TargetPath = launcherPath,
            Arguments = app.Id,
            WorkingDirectory = Path.GetDirectoryName(launcherPath)!,
            IconLocation = $"{iconPath},0"
        };

        var helper = new FakeShortcutComHelper(shortcut);
        var writeAccessService = new TrackingShortcutWriteAccessService();
        var gateway = new ShortcutComGateway(helper, Mock.Of<IShortcutFilePersistenceNative>());
        var protection = new Mock<IShortcutProtectionService>();
        var service = new BesideTargetShortcutService(
            Mock.Of<ILoggingService>(),
            protection.Object,
            new InMemoryShortcutProtectionStateStore(),
            gateway,
            writeAccessService,
            Mock.Of<IManagedShortcutLifecycleService>(),
            CreateAliasPathResolver(),
            CreateExecutableKindService(),
            Mock.Of<IProgramDataKnownPathResolver>());

        service.EnforceBesideTargetShortcuts([app], launcherPath, _ => ("User", iconPath));

        Assert.Equal(0, writeAccessService.SaveCallCount);
        Assert.Equal(0, shortcut.SaveCount);
        protection.Verify(service => service.ProtectShortcut(
            app.Id,
            shortcutPath,
            true), Times.Once);
    }

    [Fact]
    public void EnforceBesideTargetShortcuts_CommaContainingIconPathWithNonZeroIndex_RecreatesShortcut()
    {
        using var tempDir = new TempDirectory("ShortcutComGateway_CommaIcon_Index");
        var appDir = Path.Combine(tempDir.Path, "App");
        Directory.CreateDirectory(appDir);
        var iconDir = Path.Combine(tempDir.Path, "Tools, Inc");
        Directory.CreateDirectory(iconDir);
        var iconPath = Path.Combine(iconDir, "app.ico");
        File.WriteAllBytes(iconPath, []);

        var launcherDir = Path.Combine(tempDir.Path, "Current");
        Directory.CreateDirectory(launcherDir);
        var launcherPath = Path.Combine(launcherDir, "RunFence.Launcher.exe");
        var shortcutPath = Path.Combine(appDir, "app-as-User.lnk");
        File.WriteAllBytes(shortcutPath, [0x4C, 0x00, 0x00, 0x00]);

        var app = new AppEntry
        {
            Id = AppEntry.GenerateId(),
            Name = "Managed App",
            ExePath = Path.Combine(appDir, "app.exe"),
            ManageShortcuts = true
        };

        var shortcut = new FakeShortcut
        {
            TargetPath = launcherPath,
            Arguments = app.Id,
            WorkingDirectory = Path.GetDirectoryName(launcherPath)!,
            IconLocation = $"{iconPath},2"
        };

        var helper = new FakeShortcutComHelper(shortcut);
        var writeAccessService = new TrackingShortcutWriteAccessService();
        var gateway = new ShortcutComGateway(helper, Mock.Of<IShortcutFilePersistenceNative>());
        var protection = new Mock<IShortcutProtectionService>();
        var service = new BesideTargetShortcutService(
            Mock.Of<ILoggingService>(),
            protection.Object,
            new InMemoryShortcutProtectionStateStore(),
            gateway,
            writeAccessService,
            Mock.Of<IManagedShortcutLifecycleService>(),
            CreateAliasPathResolver(),
            CreateExecutableKindService(),
            Mock.Of<IProgramDataKnownPathResolver>());

        service.EnforceBesideTargetShortcuts([app], launcherPath, _ => ("User", iconPath));

        Assert.Equal(1, writeAccessService.SaveCallCount);
    }

    private static IExecutableKindService CreateExecutableKindService()
    {
        var service = new Mock<IExecutableKindService>();
        service.Setup(s => s.IsUwpExeFile(It.IsAny<string>())).Returns(false);
        service.Setup(s => s.IsKnownBrowserExe(It.IsAny<string>())).Returns(false);
        service.Setup(s => s.SuggestsBasicPrivilegeLevel(It.IsAny<string>())).Returns(false);
        return service.Object;
    }

    private static IWindowsAppsAliasPathResolver CreateAliasPathResolver()
    {
        var resolver = new Mock<IWindowsAppsAliasPathResolver>();
        resolver.Setup(s => s.IsWindowsAppsAliasPath(It.IsAny<string>())).Returns(false);
        return resolver.Object;
    }

    public sealed class FakeShortcut
    {
        public string TargetPath { get; set; } = string.Empty;
        public string? Arguments { get; set; }
        public string? WorkingDirectory { get; set; }
        public string? IconLocation { get; set; }
        public string? Description { get; set; }
        public object? Hotkey { get; set; }
        public int WindowStyle { get; set; } = 1;
        public int SaveCount { get; private set; }

        public void Save() => SaveCount++;
    }

    private sealed class FakeShortcutComHelper(FakeShortcut shortcut) : IShortcutComHelper
    {
        public T WithShortcut<T>(string shortcutPath, Func<dynamic, T> action) => action((dynamic)shortcut);

        public void WithShortcut(string shortcutPath, Action<dynamic> action) => action((dynamic)shortcut);

        public ShortcutDefinition GetShortcutDefinition(string shortcutPath)
            => new(shortcutPath, shortcut.TargetPath, shortcut.Arguments, shortcut.WorkingDirectory);
    }

    private sealed class TrackingShortcutWriteAccessService : IShortcutWriteAccessService
    {
        public int SaveCallCount { get; private set; }

        public void Save(
            string shortcutPath,
            ShortcutMutation mutation,
            ShortcutDestinationMetadataMode metadataMode,
            ShortcutContentMode contentMode)
        {
            SaveCallCount++;
        }
    }
}
