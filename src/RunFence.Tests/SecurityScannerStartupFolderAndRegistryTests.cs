using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core.Models;
using RunFence.SecurityScanner;
using Xunit;

namespace RunFence.Tests;
public sealed class SecurityScannerStartupFolderAndRegistryTests : SecurityScannerTestBase
{
    // ===== Startup Folder Tests (migrated) =====

    [Fact]
    public void RunChecks_NoFolders_NoFindings()
    {
        var scanner = CreateScanner(s => { s.ClearStartupPaths(); });

        var results = scanner.RunChecks();

        Assert.Empty(results);
    }

    public static IEnumerable<object[]> TrustedSids()
    {
        yield return [new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value];
        yield return [new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value];
        yield return ["S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"]; // TrustedInstaller
        yield return [new SecurityIdentifier(WellKnownSidType.CreatorOwnerSid, null).Value];
        yield return ["S-1-5-80-1234567890-1234567890-1234567890-1234567890-1234567890"]; // NT SERVICE\*
        yield return ["S-1-5-87-1234567890-1234567890-1234567890-1234567890-1234567890"]; // Virtual/task identity
        yield return ["S-1-5-99-1234567890-1234567890-1234567890-1234567890-1234567890"]; // RESTRICTED SERVICES\*
        yield return ["S-1-5-19"]; // LOCAL SERVICE
        yield return ["S-1-5-20"]; // NETWORK SERVICE
    }

    [Theory]
    [MemberData(nameof(TrustedSids))]
    public void RunChecks_TrustedSidNotFlagged(string trustedSid)
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (trustedSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var scanner = CreatePublicStartupScanner(dirSec);
        var results = scanner.RunChecks();

        Assert.Empty(results);
    }

    [Fact]
    public void RunChecks_NonAdminWriteOnPublicFolder_Flagged()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreatePublicStartupScanner(dirSec);
        var results = scanner.RunChecks();

        Assert.Single(results);
        Assert.Equal(StartupSecurityCategory.StartupFolder, results[0].Category);
        Assert.Equal(@"C:\ProgramData\Startup", results[0].TargetDescription);
        Assert.Equal(UserSid1, results[0].VulnerableSid);
    }

    [Fact]
    public void RunChecks_ReadOnlyAccess_NotFlagged()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.ReadAndExecute, AccessControlType.Allow));

        var scanner = CreatePublicStartupScanner(dirSec);
        var results = scanner.RunChecks();

        Assert.Empty(results);
    }

    [Fact]
    public void RunChecks_DenyAce_NotFlagged()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.FullControl, AccessControlType.Deny));

        var scanner = CreatePublicStartupScanner(dirSec);
        var results = scanner.RunChecks();

        Assert.Empty(results);
    }

    [Fact]
    public void RunChecks_DenyOverridesAllow_NotFlagged()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData | FileSystemRights.ReadAndExecute, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Deny));

        var scanner = CreatePublicStartupScanner(dirSec);
        var results = scanner.RunChecks();

        Assert.Empty(results);
    }

    [Fact]
    public void RunChecks_OwnerSidExcludedForCurrentUserFolder()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (CurrentUserSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.SetPublicStartupPath(null);
            s.SetCurrentUserStartupPath(@"C:\Users\Admin\Startup");
            s.SetInteractiveUserSid(null);
            s.AddDirectorySecurity(@"C:\Users\Admin\Startup", dirSec);
            s.AddFolderFiles(@"C:\Users\Admin\Startup");
        });

        var results = scanner.RunChecks();

        Assert.Empty(results);
    }

    [Fact]
    public void RunChecks_NonOwnerOnCurrentUserFolder_Flagged()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData | FileSystemRights.AppendData, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.SetPublicStartupPath(null);
            s.SetCurrentUserStartupPath(@"C:\Users\Admin\Startup");
            s.SetInteractiveUserSid(null);
            s.AddDirectorySecurity(@"C:\Users\Admin\Startup", dirSec);
            s.AddFolderFiles(@"C:\Users\Admin\Startup");
        });

        var results = scanner.RunChecks();

        Assert.Single(results);
        Assert.Equal(UserSid1, results[0].VulnerableSid);
    }

    [Fact]
    public void RunChecks_FileInsideFolder_Flagged()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.AppendData, AccessControlType.Allow));

        var scanner = CreatePublicStartupScanner(dirSec, s =>
        {
            s.AddFolderFiles(@"C:\ProgramData\Startup", @"C:\ProgramData\Startup\app.lnk");
            s.AddFileSecurity(@"C:\ProgramData\Startup\app.lnk", fileSec);
        });

        var results = scanner.RunChecks();

        Assert.Single(results);
        Assert.Equal(@"C:\ProgramData\Startup\app.lnk", results[0].TargetDescription);
    }

    [Fact]
    public void RunChecks_MissingFolder_Skipped()
    {
        var scanner = CreateScanner(s =>
        {
            s.SetPublicStartupPath(@"C:\NonExistent\Startup");
            s.SetCurrentUserStartupPath(null);
            s.SetInteractiveUserSid(null);
        });

        var results = scanner.RunChecks();

        Assert.Empty(results);
    }

    [Fact]
    public void RunChecks_InteractiveUserSameAsCurrent_SkipsInteractive()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.SetCurrentUserSid(CurrentUserSid);
            s.SetInteractiveUserSid(CurrentUserSid); // same as current
            s.SetPublicStartupPath(@"C:\ProgramData\Startup");
            s.SetCurrentUserStartupPath(@"C:\Users\Admin\Startup");
            s.AddDirectorySecurity(@"C:\ProgramData\Startup", dirSec);
            s.AddDirectorySecurity(@"C:\Users\Admin\Startup", dirSec);
            s.AddFolderFiles(@"C:\ProgramData\Startup");
            s.AddFolderFiles(@"C:\Users\Admin\Startup");
        });

        var results = scanner.RunChecks();

        // Should have 2 findings (public + current user), NOT 3 (no interactive duplicate)
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void RunChecks_InteractiveUserOwnerExcluded()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (InteractiveUserSid, FileSystemRights.FullControl, AccessControlType.Allow));

        // InteractiveUserSid is NOT in the admin set for this test
        var scanner = CreateScanner(s =>
        {
            s.SetAdminMemberSids(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { AdminsSid, CurrentUserSid, "S-1-5-32-544" });
            s.SetPublicStartupPath(null);
            s.SetCurrentUserStartupPath(null);
            s.SetInteractiveProfilePath(@"C:\Users\Desktop");
            s.AddDirectorySecurity(@"C:\Users\Desktop\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup", dirSec);
            s.AddFolderFiles(@"C:\Users\Desktop\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup");
        });

        var results = scanner.RunChecks();

        Assert.Empty(results);
    }

    [Fact]
    public void RunChecks_InteractiveUserFolderNonOwnerFlagged()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid2, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.SetAdminMemberSids(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { AdminsSid, CurrentUserSid, "S-1-5-32-544" });
            s.SetPublicStartupPath(null);
            s.SetCurrentUserStartupPath(null);
            s.SetInteractiveProfilePath(@"C:\Users\Desktop");
            s.AddDirectorySecurity(@"C:\Users\Desktop\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup", dirSec);
            s.AddFolderFiles(@"C:\Users\Desktop\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup");
        });

        var results = scanner.RunChecks();

        Assert.Single(results);
        Assert.Equal(UserSid2, results[0].VulnerableSid);
    }

    [Fact]
    public void RunChecks_MultipleVulnerablePrincipals_AllFlagged()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow),
            (UserSid2, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreatePublicStartupScanner(dirSec);
        var results = scanner.RunChecks();

        Assert.Equal(2, results.Count);
    }

    // ===== Registry Key Tests (migrated) =====

    [Fact]
    public void RunChecks_RegistryAdminOnly_NoFindings()
    {
        var regSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow),
            (SystemSid, RegistryRights.FullControl, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.ClearStartupPaths();
            s.AddRegistryKeySecurity(("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), regSec);
            s.AddRegistryKeySecurity(("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"), regSec);
            s.AddRegistryKeySecurity(("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), regSec);
            s.AddRegistryKeySecurity(("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"), regSec);
        });

        var results = scanner.RunChecks();

        Assert.Empty(results);
    }

    [Theory]
    [InlineData(RegistryRights.SetValue, true)] // SetValue in RunRegistryWriteRightsMask â†’ flagged
    [InlineData(RegistryRights.ChangePermissions, true)] // ChangePermissions â†’ flagged
    [InlineData(RegistryRights.TakeOwnership, true)] // TakeOwnership â†’ flagged
    [InlineData(RegistryRights.ReadKey, false)] // read-only â†’ not flagged
    [InlineData(RegistryRights.QueryValues, false)] // query only â†’ not flagged
    [InlineData(RegistryRights.CreateSubKey, false)] // CreateSubKey not in Run write mask â†’ not flagged
    public void RunChecks_RegistryUserRight_FlaggedOnlyForRunWriteRightsMask(RegistryRights rights, bool expectFlagged)
    {
        var regSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow),
            (UserSid1, rights, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.ClearStartupPaths();
            s.AddRegistryKeySecurity(("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), regSec);
        });

        var results = scanner.RunChecks();

        if (expectFlagged)
        {
            Assert.Single(results);
            Assert.Equal(StartupSecurityCategory.RegistryRunKey, results[0].Category);
            Assert.Contains("Run", results[0].TargetDescription);
            Assert.Equal(UserSid1, results[0].VulnerableSid);
        }
        else
        {
            Assert.Empty(results);
        }
    }

    [Fact]
    public void RunChecks_RegistryOwnerSidExcludedForHKCU()
    {
        var regSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow),
            (CurrentUserSid, RegistryRights.FullControl, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.ClearStartupPaths();
            s.AddRegistryKeySecurity(("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), regSec);
            s.AddRegistryKeySecurity(("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"), regSec);
        });

        var results = scanner.RunChecks();

        Assert.Empty(results);
    }

    [Fact]
    public void RunChecks_RegistryKeyMissing_Skipped()
    {
        var scanner = CreateScanner(s => { s.ClearStartupPaths(); });

        var results = scanner.RunChecks();

        Assert.Empty(results);
    }

    [Fact]
    public void RunChecks_InteractiveUserRegistryKeys()
    {
        var regSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow),
            (UserSid2, RegistryRights.SetValue | RegistryRights.CreateSubKey, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.SetAdminMemberSids(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { AdminsSid, CurrentUserSid, "S-1-5-32-544" });
            s.SetPublicStartupPath(null);
            s.SetCurrentUserStartupPath(null);
            s.AddRegistryKeySecurity(("HKU", $@"{InteractiveUserSid}\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), regSec);
        });

        var results = scanner.RunChecks();

        Assert.Single(results);
        Assert.Equal(StartupSecurityCategory.RegistryRunKey, results[0].Category);
    }

    [Fact]
    public void RunChecks_InteractiveUserRegistryOwnerExcluded()
    {
        var regSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow),
            (InteractiveUserSid, RegistryRights.FullControl, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.SetAdminMemberSids(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { AdminsSid, CurrentUserSid, "S-1-5-32-544" });
            s.SetPublicStartupPath(null);
            s.SetCurrentUserStartupPath(null);
            s.AddRegistryKeySecurity(("HKU", $@"{InteractiveUserSid}\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), regSec);
            s.AddRegistryKeySecurity(("HKU", $@"{InteractiveUserSid}\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"), regSec);
        });

        var results = scanner.RunChecks();

        Assert.Empty(results);
    }

}
