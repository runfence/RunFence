using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class ShortcutServiceTests
{
    [Fact]
    public void ParseManagedShortcutArgs_ExactId_ReturnsEmpty()
    {
        var id = AppEntry.GenerateId();

        var result = ShortcutComHelper.ParseManagedShortcutArgs(id, id);
        Assert.Equal("", result);
    }

    [Fact]
    public void ParseManagedShortcutArgs_IdWithArgs_ReturnsOriginalArgs()
    {
        var id = AppEntry.GenerateId();
        var currentArgs = $"{id} --some-arg value";

        var result = ShortcutComHelper.ParseManagedShortcutArgs(currentArgs, id);
        Assert.Equal("--some-arg value", result);
    }

    [Fact]
    public void ParseManagedShortcutArgs_IdWithSpaceOnly_ReturnsEmpty()
    {
        var id = AppEntry.GenerateId();
        var currentArgs = $"{id} ";

        var result = ShortcutComHelper.ParseManagedShortcutArgs(currentArgs, id);
        Assert.Equal("", result);
    }

    [Fact]
    public void ParseManagedShortcutArgs_WrongId_ReturnsNull()
    {
        var id = AppEntry.GenerateId();
        var currentArgs = "something-else --args";

        var result = ShortcutComHelper.ParseManagedShortcutArgs(currentArgs, id);
        Assert.Null(result);
    }

    [Fact]
    public void ParseManagedShortcutArgs_IdWithMultipleSpacedArgs_PreservesArgs()
    {
        var id = AppEntry.GenerateId();
        var originalArgs = "--file \"C:\\My Documents\\test.txt\" --verbose";
        var currentArgs = $"{id} {originalArgs}";

        var result = ShortcutComHelper.ParseManagedShortcutArgs(currentArgs, id);
        Assert.Equal(originalArgs, result);
    }

    [Fact]
    public void ParseManagedShortcutArgs_EmptyArgs_ReturnsNull()
    {
        var id = AppEntry.GenerateId();
        var result = ShortcutComHelper.ParseManagedShortcutArgs("", id);
        Assert.Null(result);
    }

    // --- IsUninstallShortcut tests ---

    [Theory]
    [InlineData("Uninstall MyApp", @"C:\MyApps\app.exe")]
    [InlineData("uninstall myapp", @"C:\MyApps\app.exe")]
    [InlineData("UNINSTALL", @"C:\MyApps\app.exe")]
    public void IsUninstallShortcut_ShortcutNameContainsUninstall_ReturnsTrue(string shortcutName, string target)
    {
        Assert.True(ShortcutComHelper.IsUninstallShortcut(shortcutName + ".lnk", target));
    }

    [Theory]
    [InlineData("MyApp", @"C:\MyApps\unins000.exe")]
    [InlineData("MyApp", @"C:\MyApps\Unins001.exe")]
    [InlineData("MyApp", @"C:\MyApps\UNINSTALLER.exe")]
    public void IsUninstallShortcut_TargetStartsWithUnins_ReturnsTrue(string shortcutName, string target)
    {
        Assert.True(ShortcutComHelper.IsUninstallShortcut(shortcutName + ".lnk", target));
    }

    [Theory]
    [InlineData("MyApp", @"C:\MyApps\myapp.exe")]
    [InlineData("Launch", @"C:\MyApps\setup.exe")]
    [InlineData("Reinstall", @"C:\MyApps\app.exe")]
    public void IsUninstallShortcut_NormalShortcut_ReturnsFalse(string shortcutName, string target)
    {
        Assert.False(ShortcutComHelper.IsUninstallShortcut(shortcutName + ".lnk", target));
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

    // --- ProtectInternalShortcut tests ---

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
            var service = new ShortcutProtectionService(log.Object);
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
        var (path, tempDir) = CreateTempShortcut();
        try
        {
            var log = new Mock<ILoggingService>();
            var service = new ShortcutProtectionService(log.Object);
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
            var service = new ShortcutProtectionService(log.Object);
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
            var service = new ShortcutProtectionService(log.Object);
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
            service = new ShortcutProtectionService(log2.Object);
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
        var service = new ShortcutProtectionService(log.Object);
        var accountSid = WindowsIdentity.GetCurrent().User!.Value;

        service.ProtectInternalShortcut(@"C:\DoesNotExist_RunFence_Test_" + Guid.NewGuid() + ".lnk", accountSid);

        log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Never);
    }
}