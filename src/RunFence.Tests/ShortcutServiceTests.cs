using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Acl;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
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
    public void EnforceBesideTargetShortcuts_ManagedShortcutWithOldWorkingDirectory_RecreatesShortcut()
    {
        using var tempDir = new TempDirectory("BesideTargetShortcutService_Enforce");
        var appDir = Path.Combine(tempDir.Path, "App");
        Directory.CreateDirectory(appDir);
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
            WorkingDirectory = @"D:\OldRunFence"
        };
        var log = new Mock<ILoggingService>();
        var protection = new Mock<IShortcutProtectionService>();
        var shortcutHelper = new FakeShortcutComHelper(shortcut);
        var service = new BesideTargetShortcutService(log.Object, protection.Object, shortcutHelper);

        service.EnforceBesideTargetShortcuts(
            [app],
            launcherPath,
            _ => ("User", iconPath: ""));

        Assert.Equal(launcherPath, shortcut.TargetPath);
        Assert.Equal(Path.GetDirectoryName(launcherPath), shortcut.WorkingDirectory);
        Assert.Equal(app.Id, shortcut.Arguments);
        Assert.Equal(1, shortcut.SaveCount);
        Assert.Equal(2, shortcutHelper.WithShortcutCount);
        Assert.All(shortcutHelper.InvokedPaths, p => Assert.Equal(shortcutPath, p));
        protection.Verify(p => p.UnprotectShortcut(shortcutPath), Times.Once);
        protection.Verify(p => p.ProtectShortcut(shortcutPath), Times.Once);
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
        fx.Protection.Verify(p => p.UnprotectShortcut(fx.ShortcutPath), Times.Once);
        fx.Protection.Verify(p => p.ProtectShortcut(fx.ShortcutPath), Times.Once);
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
        fx.Protection.Verify(p => p.UnprotectShortcut(It.IsAny<string>()), Times.Never);
        fx.Protection.Verify(p => p.ProtectShortcut(fx.ShortcutPath), Times.Once);
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
        fx.Protection.Verify(p => p.UnprotectShortcut(fx.ShortcutPath), Times.Once);
        fx.Protection.Verify(p => p.ProtectShortcut(fx.ShortcutPath), Times.Once);
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
        fx.Protection.Verify(p => p.UnprotectShortcut(fx.ShortcutPath), Times.Once);
        fx.Protection.Verify(p => p.ProtectShortcut(fx.ShortcutPath), Times.Once);
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
    public void ReplaceShortcuts_SaveSucceedsButProtectFails_UpdatesCache()
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
        protection.SetupSequence(p => p.ProtectShortcut(shortcutPath))
            .Throws(new IOException("protect failed"))
            .Pass();
        var shortcutHelper = new FakeShortcutComHelper(shortcut);
        var service = new ShortcutService(
            log.Object,
            protection.Object,
            shortcutHelper,
            new Mock<IInteractiveUserDesktopProvider>().Object);

        service.ReplaceShortcuts(app, launcherPath, iconPath: "", cache);

        var entry = Assert.Single(cache.Entries);
        Assert.Equal(launcherPath, entry.TargetPath);
        Assert.Equal($"{app.Id} --original", entry.Arguments);
        Assert.Equal(1, shortcut.SaveCount);
        Assert.All(shortcutHelper.InvokedPaths, p => Assert.Equal(shortcutPath, p));
        log.Verify(l => l.Error(
            It.Is<string>(message => message.Contains("Failed to replace shortcut")),
            It.IsAny<IOException>()), Times.Once);
    }

    public sealed class FakeShortcut
    {
        public string TargetPath { get; set; } = "";
        public string Arguments { get; set; } = "";
        public string WorkingDirectory { get; set; } = "";
        public string IconLocation { get; set; } = "";
        public int SaveCount { get; private set; }

        public void Save() => SaveCount++;
    }

    private sealed class FakeShortcutComHelper(FakeShortcut shortcut) : IShortcutComHelper
    {
        private readonly List<string> _invokedPaths = [];

        public int WithShortcutCount => _invokedPaths.Count;
        public IReadOnlyList<string> InvokedPaths => _invokedPaths;

        public T WithShortcut<T>(string path, Func<dynamic, T> action)
        {
            _invokedPaths.Add(path);
            return action(shortcut);
        }

        public void WithShortcut(string path, Action<dynamic> action)
        {
            _invokedPaths.Add(path);
            action(shortcut);
        }

        public (string? target, string? args) GetShortcutTargetAndArgs(string path)
            => throw new NotSupportedException();

    }

    private sealed record ShortcutTestFixture(
        TempDirectory TempDir,
        string ShortcutPath,
        AppEntry App,
        FakeShortcut Shortcut,
        FakeShortcutComHelper ShortcutHelper,
        Mock<ILoggingService> Log,
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
        var service = new ShortcutService(
            log.Object,
            protection.Object,
            shortcutHelper,
            new Mock<IInteractiveUserDesktopProvider>().Object);

        return new ShortcutTestFixture(tempDir, shortcutPath, app, shortcut, shortcutHelper,
            log, protection, service);
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
            var service = new ShortcutProtectionService(log.Object, new AclAccessor());
            var accountSid = WindowsIdentity.GetCurrent().User!.Value;

            service.ProtectInternalShortcut(path, accountSid);

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
    public void ProtectInternalShortcut_ReadOnlyNotSet_WhenAclRevokesCallerWriteAccess()
    {
        // ProtectInternalShortcut attempts to set ReadOnly after the strict ACL is applied.
        // When running as a non-admin user, the strict ACL (LocalSystem + Admins + accountSid=ReadAndExecute)
        // removes the caller's write access, so SetAttributes fails — ReadOnly is not set and the
        // error is logged. This test verifies that documented behaviour: ReadOnly is not set and
        // exactly one error is logged (no crash, no swallowed failure).
        // Skip when running as admin: admins retain write access after ACL change, so SetAttributes
        // succeeds (ReadOnly IS set) and no error is logged — the admin behavior is by design.
        if (new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            throw Xunit.Sdk.SkipException.ForSkip("Test requires non-admin runner — admin retains write access after strict ACL change, so ReadOnly IS set and no error logged.");
        var (path, tempDir) = CreateTempShortcut();
        try
        {
            var log = new Mock<ILoggingService>();
            var service = new ShortcutProtectionService(log.Object, new AclAccessor());
            var accountSid = WindowsIdentity.GetCurrent().User!.Value;

            service.ProtectInternalShortcut(path, accountSid);

            Assert.True((File.GetAttributes(path) & FileAttributes.ReadOnly) == 0,
                "File should not be read-only: SetAttributes fails when caller's write access is revoked by the strict ACL");
            log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
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
            var service = new ShortcutProtectionService(log.Object, new AclAccessor());
            var accountSid = WindowsIdentity.GetCurrent().User!.Value;

            service.ProtectInternalShortcut(path, accountSid);

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
            var service = new ShortcutProtectionService(log.Object, new AclAccessor());
            var accountSid = WindowsIdentity.GetCurrent().User!.Value;

            service.ProtectInternalShortcut(path, accountSid);

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
            service = new ShortcutProtectionService(log2.Object, new AclAccessor());
            service.ProtectInternalShortcut(path, accountSid); // second call on already-protected file

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

    [Fact]
    public void ProtectInternalShortcut_NonExistentFile_IsNoOp()
    {
        var log = new Mock<ILoggingService>();
        var service = new ShortcutProtectionService(log.Object, new AclAccessor());
        var accountSid = WindowsIdentity.GetCurrent().User!.Value;

        service.ProtectInternalShortcut(@"C:\DoesNotExist_RunFence_Test_" + Guid.NewGuid() + ".lnk", accountSid);

        log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Never);
    }
}
