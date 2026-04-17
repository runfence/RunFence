using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.SecurityScanner;
using Xunit;
using Scanner = RunFence.SecurityScanner.SecurityScanner;

namespace RunFence.Tests;

public class SecurityScannerTests
{
    // Well-known SIDs for testing
    private static readonly string AdminsSid = new SecurityIdentifier(
        WellKnownSidType.BuiltinAdministratorsSid, null).Value;

    private static readonly string SystemSid = new SecurityIdentifier(
        WellKnownSidType.LocalSystemSid, null).Value;

    private const string TrustedInstallerSid =
        "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    // Fake non-admin user SIDs
    private const string UserSid1 = "S-1-5-21-1111111111-2222222222-3333333333-1001";
    private const string UserSid2 = "S-1-5-21-1111111111-2222222222-3333333333-1002";
    private const string CurrentUserSid = "S-1-5-21-999999999-888888888-777777777-500";
    private const string InteractiveUserSid = "S-1-5-21-444444444-555555555-666666666-1001";

    // Admin member SID (individual admin user)
    private const string AdminUserSid = "S-1-5-21-1111111111-2222222222-3333333333-500";

    private Scanner CreateScanner(Action<TestScannerDataAccess>? configure = null)
    {
        var dataAccess = new TestScannerDataAccess();
        dataAccess.SetCurrentUserSid(CurrentUserSid);
        dataAccess.SetInteractiveUserSid(InteractiveUserSid);
        dataAccess.SetAdminMemberSids(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AdminsSid, CurrentUserSid, "S-1-5-32-544"
        });
        configure?.Invoke(dataAccess);
        return new Scanner(dataAccess);
    }

    private static Scanner CreateIsolatedScanner(Action<TestScannerDataAccess>? configure = null)
    {
        var dataAccess = new TestScannerDataAccess();
        dataAccess.SetCurrentUserSid(null);
        dataAccess.SetAdminMemberSids(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AdminsSid, "S-1-5-32-544"
        });
        dataAccess.ClearStartupPaths();
        configure?.Invoke(dataAccess);
        return new Scanner(dataAccess);
    }

    /// <summary>
    /// Creates a scanner with public startup folder configured, no current-user or interactive folders.
    /// </summary>
    private Scanner CreatePublicStartupScanner(DirectorySecurity dirSec,
        Action<TestScannerDataAccess>? configure = null)
    {
        return CreateScanner(s =>
        {
            s.SetPublicStartupPath(@"C:\ProgramData\Startup");
            s.SetCurrentUserStartupPath(null);
            s.SetInteractiveUserSid(null);
            s.AddDirectorySecurity(@"C:\ProgramData\Startup", dirSec);
            s.AddFolderFiles(@"C:\ProgramData\Startup");
            configure?.Invoke(s);
        });
    }

    // --- Helper: create security descriptors ---

    private static DirectorySecurity CreateDirSecurity(params (string Sid, FileSystemRights Rights, AccessControlType Type)[] aces)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var (sid, rights, type) in aces)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid), rights, type));
        }

        return security;
    }

    private static FileSecurity CreateFileSecurity(params (string Sid, FileSystemRights Rights, AccessControlType Type)[] aces)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var (sid, rights, type) in aces)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid), rights, type));
        }

        return security;
    }

    private static RegistrySecurity CreateRegSecurity(params (string Sid, RegistryRights Rights, AccessControlType Type)[] aces)
    {
        var security = new RegistrySecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var (sid, rights, type) in aces)
        {
            security.AddAccessRule(new RegistryAccessRule(
                new SecurityIdentifier(sid), rights, type));
        }

        return security;
    }

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
    [InlineData(RegistryRights.SetValue, true)] // SetValue in RunRegistryWriteRightsMask → flagged
    [InlineData(RegistryRights.ChangePermissions, true)] // ChangePermissions → flagged
    [InlineData(RegistryRights.TakeOwnership, true)] // TakeOwnership → flagged
    [InlineData(RegistryRights.ReadKey, false)] // read-only → not flagged
    [InlineData(RegistryRights.QueryValues, false)] // query only → not flagged
    [InlineData(RegistryRights.CreateSubKey, false)] // CreateSubKey not in Run write mask → not flagged
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

    // ===== Autorun executable tests (migrated) =====

    [Fact]
    public void RunChecks_AutorunExeWritable_Flagged()
    {
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));
        var parentDirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));
        var adminOnlyRegSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.ClearStartupPaths();
            s.AddRegistryKeySecurity(("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), adminOnlyRegSec);
            s.AddRegistryAutorunPaths(("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                [@"C:\Program Files\App\app.exe"]);
            s.AddFileExists(@"C:\Program Files\App\app.exe");
            s.AddFileSecurity(@"C:\Program Files\App\app.exe", fileSec);
            s.AddDirectorySecurity(@"C:\Program Files\App", parentDirSec);
        });

        var results = scanner.RunChecks();

        Assert.Single(results);
        Assert.Equal(StartupSecurityCategory.RegistryRunKey, results[0].Category);
        Assert.Equal(@"C:\Program Files\App\app.exe", results[0].TargetDescription);
    }

    [Fact]
    public void RunChecks_AutorunExeDeleteOnly_NotFlagged()
    {
        // Delete alone (without parent WriteData) is not exploitable
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.Delete, AccessControlType.Allow));
        var parentDirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.ClearStartupPaths();
            s.AddRegistryAutorunPaths(("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                [@"C:\App\autorun.exe"]);
            s.AddFileExists(@"C:\App\autorun.exe");
            s.AddFileSecurity(@"C:\App\autorun.exe", fileSec);
            s.AddDirectorySecurity(@"C:\App", parentDirSec);
        });

        var results = scanner.RunChecks();

        Assert.Empty(results);
    }

    [Fact]
    public void RunChecks_AutorunExeReplaceable_FlaggedAsFile()
    {
        // Delete on file + WriteData on parent = replaceable. Reported as a file finding.
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.Delete, AccessControlType.Allow));
        var parentDirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));
        var adminOnlyRegSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.ClearStartupPaths();
            s.AddRegistryKeySecurity(("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), adminOnlyRegSec);
            s.AddRegistryAutorunPaths(("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                [@"C:\Program Files\App\app.exe"]);
            s.AddFileExists(@"C:\Program Files\App\app.exe");
            s.AddFileSecurity(@"C:\Program Files\App\app.exe", fileSec);
            s.AddDirectorySecurity(@"C:\Program Files\App", parentDirSec);
        });

        var results = scanner.RunChecks();

        Assert.Single(results);
        Assert.Equal(@"C:\Program Files\App\app.exe", results[0].TargetDescription);
        Assert.Contains("Replaceable", results[0].AccessDescription);
    }

    [Fact]
    public void RunChecks_AutorunExeDirectWriteAndReplaceable_OnlyDirectFlagged()
    {
        // SID has both direct WriteData AND Delete+parent-WriteData.
        // Only the direct write finding should appear — replacement is redundant.
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData | FileSystemRights.Delete, AccessControlType.Allow));
        var parentDirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));
        var adminOnlyRegSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.ClearStartupPaths();
            s.AddRegistryKeySecurity(("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), adminOnlyRegSec);
            s.AddRegistryAutorunPaths(("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                [@"C:\Program Files\App\app.exe"]);
            s.AddFileExists(@"C:\Program Files\App\app.exe");
            s.AddFileSecurity(@"C:\Program Files\App\app.exe", fileSec);
            s.AddDirectorySecurity(@"C:\Program Files\App", parentDirSec);
        });

        var results = scanner.RunChecks();

        Assert.Single(results);
        Assert.DoesNotContain("Replaceable", results[0].AccessDescription);
    }

    [Fact]
    public void RunChecks_AutorunExeInsideAutorunLocation_NoReplacementCheck()
    {
        // Files inside autorun locations don't get the replacement scenario check
        // (the container itself is what matters, not individual file replaceability)
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.Delete, AccessControlType.Allow));
        var parentDirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.SetPublicStartupPath(@"C:\ProgramData\Startup");
            s.AddDirectorySecurity(@"C:\ProgramData\Startup", parentDirSec);
            s.AddFolderFiles(@"C:\ProgramData\Startup", @"C:\ProgramData\Startup\app.exe");
            s.AddFileExists(@"C:\ProgramData\Startup\app.exe");
            s.AddFileSecurity(@"C:\ProgramData\Startup\app.exe", fileSec);
        });

        var results = scanner.RunChecks();

        // No replacement finding — exe is inside autorun location
        Assert.DoesNotContain(results, r =>
            r.Category == StartupSecurityCategory.StartupFolder &&
            r.AccessDescription.Contains("Replaceable"));
    }

    [Fact]
    public void RunChecks_AutorunExeTrustedOnly_NotFlagged()
    {
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (SystemSid, FileSystemRights.FullControl, AccessControlType.Allow));
        var parentDirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (TrustedInstallerSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.ClearStartupPaths();
            s.AddRegistryAutorunPaths(("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                [@"C:\Windows\System32\app.exe"]);
            s.AddFileExists(@"C:\Windows\System32\app.exe");
            s.AddFileSecurity(@"C:\Windows\System32\app.exe", fileSec);
            s.AddDirectorySecurity(@"C:\Windows\System32", parentDirSec);
        });

        var results = scanner.RunChecks();

        Assert.Empty(results);
    }

    [Fact]
    public void RunChecks_AutorunExeNotFound_Skipped()
    {
        var scanner = CreateScanner(s =>
        {
            s.ClearStartupPaths();
            s.AddRegistryAutorunPaths(("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                [@"C:\Missing\app.exe"]);
        });

        var results = scanner.RunChecks();

        Assert.Empty(results);
    }

    // ===== Group member suppression tests =====

    private const string GroupSid1 = "S-1-5-32-545"; // BUILTIN\Users

    [Fact]
    public void RunChecks_GroupWithOnlyExcludedMembers_Suppressed()
    {
        // Group whose only member is an admin → should not be flagged
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (GroupSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.SetPublicStartupPath(@"C:\ProgramData\Startup");
            s.AddDirectorySecurity(@"C:\ProgramData\Startup", dirSec);
            s.SetAdminMemberSids(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { AdminUserSid, "S-1-5-32-544" });
            s.SetGroupMemberSids(GroupSid1, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { AdminUserSid });
        });

        var results = scanner.RunChecks();

        Assert.DoesNotContain(results, r => r.VulnerableSid == GroupSid1);
    }

    [Fact]
    public void RunChecks_GroupWithNonExcludedMember_NotSuppressed()
    {
        // Group has a member not in exclusions — should be flagged
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (GroupSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.SetPublicStartupPath(@"C:\ProgramData\Startup");
            s.AddDirectorySecurity(@"C:\ProgramData\Startup", dirSec);
            s.SetGroupMemberSids(GroupSid1, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { UserSid1 });
        });

        var results = scanner.RunChecks();

        Assert.Contains(results, r => r.VulnerableSid == GroupSid1);
    }

    [Fact]
    public void RunChecks_EmptyGroup_NotSuppressed()
    {
        // Group with empty member list — member enumeration may have failed; do not suppress
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (GroupSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.SetPublicStartupPath(@"C:\ProgramData\Startup");
            s.AddDirectorySecurity(@"C:\ProgramData\Startup", dirSec);
            s.SetGroupMemberSids(GroupSid1, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        });

        var results = scanner.RunChecks();

        Assert.Contains(results, r => r.VulnerableSid == GroupSid1);
    }

    [Fact]
    public void RunChecks_NonGroupSid_NotSuppressed()
    {
        // SID that can't be resolved as a group — should be flagged normally
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.SetPublicStartupPath(@"C:\ProgramData\Startup");
            s.AddDirectorySecurity(@"C:\ProgramData\Startup", dirSec);
            // No SetGroupMemberSids for UserSid1 → TryGetGroupMemberSids returns null
        });

        var results = scanner.RunChecks();

        Assert.Contains(results, r => r.VulnerableSid == UserSid1);
    }

    [Theory]
    [InlineData("S-1-5-4")] // INTERACTIVE
    [InlineData("S-1-5-11")] // Authenticated Users
    [InlineData("S-1-5-2")] // NETWORK
    public void RunChecks_ImplicitGroupEmptyMembers_NotSuppressed(string implicitSid)
    {
        // Implicit groups (INTERACTIVE, Authenticated Users, NETWORK) appear as groups
        // with 0 enumerable members but effectively include many users at logon time.
        // They must NOT be suppressed.
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (implicitSid, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.SetPublicStartupPath(@"C:\ProgramData\Startup");
            s.AddDirectorySecurity(@"C:\ProgramData\Startup", dirSec);
            s.SetGroupMemberSids(implicitSid, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        });

        var results = scanner.RunChecks();

        Assert.Contains(results, r => r.VulnerableSid == implicitSid);
    }

    [Fact]
    public void RunChecks_GroupWithOnlyTrustedSystemMembers_Suppressed()
    {
        // Group whose only member is a trusted system SID — should be suppressed
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (GroupSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.SetPublicStartupPath(@"C:\ProgramData\Startup");
            s.AddDirectorySecurity(@"C:\ProgramData\Startup", dirSec);
            // S-1-5-18 = SYSTEM, a trusted system SID
            s.SetGroupMemberSids(GroupSid1, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "S-1-5-18" });
        });

        var results = scanner.RunChecks();

        Assert.DoesNotContain(results, r => r.VulnerableSid == GroupSid1);
    }

    /// <summary>
    /// Tests whether a group finding is suppressed based on the individual ACE coverage of its member.
    /// Parameters: userIndividualRights (null = no individual ACE), expectGroupFlagged, expectUserFlagged.
    /// </summary>
    public static IEnumerable<object?[]> GroupMemberCoverageData()
    {
        // Member individually has write rights → group is redundant (suppressed), user individually reported
        yield return [FileSystemRights.WriteData, false, true];
        // Member individually has read-only rights → group NOT suppressed (covers write not in individual ACE)
        yield return [FileSystemRights.ReadAndExecute, true, false];
        // Member has no individual ACE → group NOT suppressed (only path to write access)
        yield return [null, true, false];
    }

    [Theory]
    [MemberData(nameof(GroupMemberCoverageData))]
    public void RunChecks_GroupSuppression_BasedOnMemberIndividualCoverage(
        FileSystemRights? userIndividualRights, bool expectGroupFlagged, bool expectUserFlagged)
    {
        var aces = new List<(string, FileSystemRights, AccessControlType)>
        {
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (GroupSid1, FileSystemRights.WriteData, AccessControlType.Allow)
        };
        if (userIndividualRights.HasValue)
            aces.Add((UserSid1, userIndividualRights.Value, AccessControlType.Allow));

        var dirSec = CreateDirSecurity([.. aces]);

        var scanner = CreateScanner(s =>
        {
            s.SetPublicStartupPath(@"C:\ProgramData\Startup");
            s.AddDirectorySecurity(@"C:\ProgramData\Startup", dirSec);
            s.SetGroupMemberSids(GroupSid1, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { UserSid1 });
        });

        var results = scanner.RunChecks();

        if (expectGroupFlagged)
            Assert.Contains(results, r => r.VulnerableSid == GroupSid1);
        else
            Assert.DoesNotContain(results, r => r.VulnerableSid == GroupSid1);

        if (expectUserFlagged)
            Assert.Contains(results, r => r.VulnerableSid == UserSid1);
        else
            Assert.DoesNotContain(results, r => r.VulnerableSid == UserSid1);
    }

    // ===== Admin exclusion tests =====

    [Fact]
    public void RunChecks_AdminMemberSidExcluded()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (AdminUserSid, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreatePublicStartupScanner(dirSec, s =>
        {
            s.SetAdminMemberSids(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { AdminsSid, CurrentUserSid, AdminUserSid, "S-1-5-32-544" });
        });

        var results = scanner.RunChecks();

        Assert.Empty(results);
    }

    // ===== Admin owner + interactive user test =====

    [Fact]
    public void RunChecks_AdminOwnerDoesNotExcludeInteractive()
    {
        // When folder owner is an admin, interactive SID should NOT be excluded (priv escalation risk)
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (InteractiveUserSid, FileSystemRights.WriteData, AccessControlType.Allow));

        // Set up so CurrentUser is an admin, and InteractiveUserSid is NOT in admin set
        var scanner = CreateScanner(s =>
        {
            s.SetAdminMemberSids(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { AdminsSid, CurrentUserSid, "S-1-5-32-544" });
            s.SetPublicStartupPath(null);
            s.SetCurrentUserStartupPath(@"C:\Users\Admin\Startup");
            s.AddDirectorySecurity(@"C:\Users\Admin\Startup", dirSec);
            s.AddFolderFiles(@"C:\Users\Admin\Startup");
        });

        var results = scanner.RunChecks();

        // Interactive user should NOT be excluded because the owner (CurrentUserSid) is an admin
        // In BuildPerUserExcluded: ownerSid (CurrentUserSid) IS in adminSids, so interactive is NOT added
        Assert.Single(results);
        Assert.Equal(InteractiveUserSid, results[0].VulnerableSid);
    }

    // ===== Service tests =====

    [Fact]
    public void RunChecks_ServiceRegistryKeyWritable_Flagged()
    {
        var regSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow),
            (UserSid1, RegistryRights.SetValue, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddServiceEntry("TestSvc", @"""C:\Svc\test.exe""", @"C:\Svc\test.exe");
            s.AddServiceRegistryKeySecurity("TestSvc", regSec);
        });

        var results = scanner.RunChecks();

        var svcFindings = results.Where(f => f.Category == StartupSecurityCategory.AutoStartService).ToList();
        Assert.Single(svcFindings);
        Assert.Contains("TestSvc", svcFindings[0].TargetDescription);
    }

    // ===== Winlogon tests =====

    [Fact]
    public void RunChecks_WinlogonKeyWritable_Flagged()
    {
        var regSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow),
            (UserSid1, RegistryRights.SetValue, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s => s.SetWinlogonRegistryKeySecurity(regSec));

        var results = scanner.RunChecks();

        var winlogonFindings = results.Where(f =>
            f.Category == StartupSecurityCategory.RegistryRunKey &&
            f.TargetDescription.Contains("Winlogon")).ToList();
        Assert.Single(winlogonFindings);
    }

    // ===== ExtractExecutablePath tests (migrated) =====

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" -arg", "C:\\Program Files\\App\\app.exe")]
    [InlineData("C:\\Windows\\notepad.exe", "C:\\Windows\\notepad.exe")]
    [InlineData("C:\\Program Files\\app.exe -silent", "C:\\Program Files\\app.exe")]
    [InlineData("app.exe /arg1 /arg2", "app.exe")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("C:\\Program Files\\script.cmd -arg", "C:\\Program Files\\script.cmd")]
    [InlineData("C:\\Program Files\\old.bat /run", "C:\\Program Files\\old.bat")]
    [InlineData("C:\\tools\\legacy.com /s", "C:\\tools\\legacy.com")]
    public void ExtractExecutablePath_VariousFormats(string input, string? expected)
    {
        var result = CommandLineParser.ExtractExecutablePath(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractExecutablePath_ExpandsEnvironmentVariables()
    {
        var result = CommandLineParser.ExtractExecutablePath("%SystemRoot%\\System32\\cmd.exe /c start");

        Assert.NotNull(result);
        Assert.DoesNotContain("%", result);
        Assert.EndsWith("cmd.exe", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractExecutablePath_ExpandsEnvironmentVariables_Quoted()
    {
        var result = CommandLineParser.ExtractExecutablePath("\"%ProgramFiles%\\App\\app.exe\" -arg");

        Assert.NotNull(result);
        Assert.DoesNotContain("%", result);
        Assert.EndsWith("app.exe", result, StringComparison.OrdinalIgnoreCase);
    }

    // ===== cmd wrapper extraction tests =====

    [Theory]
    // cmd /c "start [/B] path" — quoted inner command with start
    [InlineData("cmd /c \"start /B C:\\tools\\app.exe\"", "C:\\tools\\app.exe")]
    [InlineData("cmd /c \"start C:\\tools\\app.exe\"", "C:\\tools\\app.exe")]
    [InlineData("cmd.exe /c \"start /B C:\\tools\\app.exe\"", "C:\\tools\\app.exe")]
    // cmd /c "[path]" — quoted path directly
    [InlineData("cmd /c \"C:\\tools\\script.bat\"", "C:\\tools\\script.bat")]
    [InlineData("C:\\Windows\\System32\\cmd.exe /c \"C:\\tools\\script.bat\"", "C:\\tools\\script.bat")]
    [InlineData("\"C:\\Windows\\System32\\cmd.exe\" /c \"C:\\tools\\script.bat\"", "C:\\tools\\script.bat")]
    // cmd /c path — unquoted path
    [InlineData("cmd /c C:\\tools\\app.exe", "C:\\tools\\app.exe")]
    [InlineData("cmd /c script.bat", "script.bat")]
    // cmd /c start /B path — unquoted start with flag
    [InlineData("cmd /c start /B C:\\tools\\app.exe", "C:\\tools\\app.exe")]
    [InlineData("cmd /c start C:\\tools\\app.exe", "C:\\tools\\app.exe")]
    // cmd without /c — bare path
    [InlineData("cmd script.bat", "script.bat")]
    // cmd /c "path with spaces" — quoted path with spaces, with or without args
    [InlineData("cmd /c \"C:\\Program Files\\App\\app.exe\"", "C:\\Program Files\\App\\app.exe")]
    [InlineData("cmd /c \"C:\\Program Files\\App\\app.exe -arg\"", "C:\\Program Files\\App\\app.exe")]
    // cmd /c start with title before path
    [InlineData("cmd /c \"start MyTitle C:\\tools\\app.exe\"", "C:\\tools\\app.exe")]
    // cmd with leading switches before /c
    [InlineData("cmd /q /c C:\\tools\\app.exe", "C:\\tools\\app.exe")]
    public void ExtractExecutablePath_CmdWrapper(string input, string expected)
    {
        var result = CommandLineParser.ExtractExecutablePath(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractExecutablePath_CmdNoTarget_ReturnsCmdItself()
    {
        // cmd /c start with no path — falls back to cmd executable
        var result = CommandLineParser.ExtractExecutablePath("cmd.exe /c start");
        Assert.NotNull(result);
        Assert.Equal("cmd.exe", result, StringComparer.OrdinalIgnoreCase);
    }

    // ===== powershell wrapper extraction tests =====

    [Theory]
    // positional script path (with or without extension)
    [InlineData("powershell C:\\scripts\\run.ps1", "C:\\scripts\\run.ps1")]
    [InlineData("powershell.exe C:\\scripts\\run.ps1", "C:\\scripts\\run.ps1")]
    [InlineData("pwsh C:\\scripts\\run.ps1", "C:\\scripts\\run.ps1")]
    // -File flag
    [InlineData("powershell -File C:\\scripts\\run.ps1", "C:\\scripts\\run.ps1")]
    [InlineData("powershell.exe -File \"C:\\scripts\\run.ps1\"", "C:\\scripts\\run.ps1")]
    [InlineData("powershell -f C:\\scripts\\run.ps1", "C:\\scripts\\run.ps1")]
    // -File after other flags
    [InlineData("powershell -NoProfile -File C:\\scripts\\run.ps1", "C:\\scripts\\run.ps1")]
    [InlineData("powershell -NoProfile -ExecutionPolicy Bypass -File C:\\scripts\\run.ps1", "C:\\scripts\\run.ps1")]
    [InlineData("powershell -NonInteractive -NoLogo -ExecutionPolicy RemoteSigned -File \"C:\\scripts\\run.ps1\"", "C:\\scripts\\run.ps1")]
    // -Command with & 'path' pattern
    [InlineData("powershell -Command \"& 'C:\\scripts\\run.ps1'\"", "C:\\scripts\\run.ps1")]
    public void ExtractExecutablePath_PowerShellWrapper(string input, string expected)
    {
        var result = CommandLineParser.ExtractExecutablePath(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractExecutablePath_PowerShellNoScript_ReturnsPowerShellItself()
    {
        // powershell with only flags and no script — falls back to powershell executable
        var result = CommandLineParser.ExtractExecutablePath("powershell.exe -NoProfile -NonInteractive");
        Assert.NotNull(result);
        Assert.Equal("powershell.exe", result, StringComparer.OrdinalIgnoreCase);
    }

    // ===== Unquoted service path tests =====

    [Fact]
    public void ComputeUnquotedPathCandidates_QuotedPath_NoCandidates()
    {
        var candidates = CommandLineParser.ComputeUnquotedPathCandidates("\"C:\\Program Files\\App\\svc.exe\"");
        Assert.Empty(candidates);
    }

    [Fact]
    public void ComputeUnquotedPathCandidates_UnquotedPath_HasCandidates()
    {
        var candidates = CommandLineParser.ComputeUnquotedPathCandidates("C:\\Program Files\\App\\svc.exe -arg");
        Assert.NotEmpty(candidates);
    }

    // ===== CancellationToken test =====

    [Fact]
    public void RunChecks_RespectsCancellation()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var scanner = CreateIsolatedScanner();
        Assert.Throws<OperationCanceledException>(() => scanner.RunChecks(cts.Token));
    }

    // ===== Rights mask non-flagging tests =====

    [Fact]
    public void RunChecks_WriteAttributesAlone_NotFlaggedOnContainer()
    {
        // WriteAttributes is NOT in ContainerWriteRightsMask
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteAttributes, AccessControlType.Allow));

        var scanner = CreatePublicStartupScanner(dirSec);
        var results = scanner.RunChecks();

        Assert.Empty(results);
    }

    [Fact]
    public void RunChecks_DeleteSubdirsAlone_NotFlaggedOnContainer()
    {
        // DeleteSubdirectoriesAndFiles is excluded from ContainerWriteRightsMask:
        // deleting from a container without WriteData cannot cause privilege escalation
        // since no new executable can be placed.
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.DeleteSubdirectoriesAndFiles, AccessControlType.Allow));

        var scanner = CreatePublicStartupScanner(dirSec);
        var results = scanner.RunChecks();

        Assert.Empty(results);
    }

    // ===== Unresolvable SID test =====

    [Fact]
    public void RunChecks_UnresolvableSid_ReportedWithSuffix()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreatePublicStartupScanner(dirSec);
        var results = scanner.RunChecks();

        Assert.Single(results);
        // In tests, ResolveDisplayName returns the SID string, so CachedResolveDisplayName appends suffix
        Assert.Contains("(unknown SID)", results[0].VulnerablePrincipal);
    }

    // ===== Task Scheduler tests =====
    // Task Scheduler SDs (both folder and individual task) are NOT checked. SchRpcRegisterTask
    // enforces a principal-based security matrix (MS-TSCH 3.2.5.1.1) that blocks non-elevated
    // callers from creating/modifying elevated tasks regardless of SD permissions. Folder
    // ChangePermissions/TakeOwnership can only chain to CreateTask which is limited to the
    // caller's own account. Task exe paths are still collected for autorun checks.

    [Fact]
    public void RunChecks_TaskSchedulerExePathsStillCollected()
    {
        // Task executable paths are collected for autorun checks and findings
        // are attributed back to the TaskScheduler category.
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.ClearStartupPaths();
            s.SetTaskSchedulerData(
            [
                new ScheduledTaskInfo(@"\TaskWithExe", @"\", "TaskWithExe",
                    [@"C:\Program Files\app.exe"], false, null)
            ]);
            s.AddFileExists(@"C:\Program Files\app.exe");
            s.AddFileSecurity(@"C:\Program Files\app.exe", fileSec);
        });

        var results = scanner.RunChecks();

        var taskFindings = results.Where(f =>
            f is { Category: StartupSecurityCategory.TaskScheduler, VulnerableSid: UserSid1 }).ToList();
        Assert.NotEmpty(taskFindings);
    }

    [Fact]
    public void RunChecks_TaskSchedulerPerUserExePathsHaveOwnerExclusion()
    {
        // Per-user task exe paths should have owner exclusion applied.
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.ClearStartupPaths();
            s.SetTaskSchedulerData(
            [
                new ScheduledTaskInfo(@"\MyTask", @"\", "MyTask",
                    [@"C:\Program Files\app.exe"], true, UserSid1)
            ]);
            s.AddFileExists(@"C:\Program Files\app.exe");
            s.AddFileSecurity(@"C:\Program Files\app.exe", fileSec);
        });

        var results = scanner.RunChecks();

        // UserSid1 should be excluded as task owner — no autorun finding
        var autorunFindings = results.Where(f =>
            f is { Category: StartupSecurityCategory.TaskScheduler, VulnerableSid: UserSid1 }).ToList();
        Assert.Empty(autorunFindings);
    }

    // ===== IFEO tests =====

    [Fact]
    public void RunChecks_IfeoKeyWritable_Flagged()
    {
        var regSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow),
            (UserSid1, RegistryRights.CreateSubKey, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s => s.SetIfeoRegistryKeySecurity(regSec));

        var results = scanner.RunChecks();

        var ifeoFindings = results.Where(f =>
            f.Category == StartupSecurityCategory.RegistryRunKey &&
            f.TargetDescription.Contains("Image File Execution Options")).ToList();
        Assert.Single(ifeoFindings);
        Assert.StartsWith("HKEY_LOCAL_MACHINE", ifeoFindings[0].NavigationTarget);
    }

    // ===== IFEO per-subkey tests =====

    [Fact]
    public void RunChecks_IfeoSubkeyDebugger_CollectedAsAutorun()
    {
        // A debugger registered under an IFEO subkey (e.g. notepad.exe → debugger.exe)
        // is collected as an autorun path and flagged if its executable is writable.
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));
        var parentDirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddIfeoSubkeyName("notepad.exe");
            s.SetIfeoDebuggerPath("notepad.exe", @"C:\Tools\debugger.exe");
            s.AddFileExists(@"C:\Tools\debugger.exe");
            s.AddFileSecurity(@"C:\Tools\debugger.exe", fileSec);
            s.AddDirectorySecurity(@"C:\Tools", parentDirSec);
        });

        var results = scanner.RunChecks();

        // The debugger exe is writable → autorun finding
        var ifeoAutorunFindings = results.Where(f =>
            f is { Category: StartupSecurityCategory.RegistryRunKey, TargetDescription: @"C:\Tools\debugger.exe" }).ToList();
        Assert.Single(ifeoAutorunFindings);
        Assert.Equal(UserSid1, ifeoAutorunFindings[0].VulnerableSid);
    }

    [Fact]
    public void RunChecks_IfeoSubkeyDebuggerAdminOnly_NotFlagged()
    {
        // Admin-only debugger binary → no finding
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));
        var parentDirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddIfeoSubkeyName("calc.exe");
            s.SetIfeoDebuggerPath("calc.exe", @"C:\Windows\System32\ntvdm.exe");
            s.AddFileExists(@"C:\Windows\System32\ntvdm.exe");
            s.AddFileSecurity(@"C:\Windows\System32\ntvdm.exe", fileSec);
            s.AddDirectorySecurity(@"C:\Windows\System32", parentDirSec);
        });

        var results = scanner.RunChecks();

        Assert.DoesNotContain(results, f => f.TargetDescription.Contains("ntvdm.exe"));
    }

    [Fact]
    public void RunChecks_MultipleIfeoSubkeys_EachDebuggerChecked()
    {
        // Multiple IFEO subkeys with different debugger paths are each independently checked.
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));
        var safeFileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));
        var parentDirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddIfeoSubkeyName("app1.exe");
            s.SetIfeoDebuggerPath("app1.exe", @"C:\Tools\dbg1.exe");
            s.AddIfeoSubkeyName("app2.exe");
            s.SetIfeoDebuggerPath("app2.exe", @"C:\Tools\dbg2.exe");

            // dbg1.exe is writable, dbg2.exe is admin-only
            s.AddFileExists(@"C:\Tools\dbg1.exe");
            s.AddFileSecurity(@"C:\Tools\dbg1.exe", fileSec);
            s.AddFileExists(@"C:\Tools\dbg2.exe");
            s.AddFileSecurity(@"C:\Tools\dbg2.exe", safeFileSec);
            s.AddDirectorySecurity(@"C:\Tools", parentDirSec);
        });

        var results = scanner.RunChecks();

        // Only dbg1 flagged (dbg2 is admin-only)
        Assert.Single(results, f => f.TargetDescription == @"C:\Tools\dbg1.exe");
        Assert.DoesNotContain(results, f => f.TargetDescription == @"C:\Tools\dbg2.exe");
    }

    [Fact]
    public void RunChecks_IfeoSubkeyNoDebugger_NoAutorunCollected()
    {
        // A subkey with no Debugger value should not produce any autorun finding.
        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddIfeoSubkeyName("winword.exe");
            // No SetIfeoDebuggerPath — GetIfeoDebuggerPath returns null
        });

        var results = scanner.RunChecks();

        // No findings for a subkey with no debugger path
        Assert.DoesNotContain(results, f => f.Category == StartupSecurityCategory.RegistryRunKey);
    }

    // ===== GP/logon script tests =====

    [Fact]
    public void RunChecks_MachineGpScriptsDir_WritableFlagged()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetMachineGpScriptsDir(@"C:\Windows\System32\GroupPolicy\Machine\Scripts");
            s.AddDirectorySecurity(@"C:\Windows\System32\GroupPolicy\Machine\Scripts", dirSec);
        });

        var results = scanner.RunChecks();

        var scriptFindings = results.Where(f => f.Category == StartupSecurityCategory.LogonScript).ToList();
        Assert.Single(scriptFindings);
        Assert.Equal(UserSid1, scriptFindings[0].VulnerableSid);
    }

    // ===== Shortcut resolution test =====

    [Fact]
    public void RunChecks_ShortcutTargetResolved_CheckedAsAutorun()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));
        var parentDirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var scanner = CreatePublicStartupScanner(dirSec, s =>
        {
            s.AddFolderFiles(@"C:\ProgramData\Startup", @"C:\ProgramData\Startup\app.lnk");
            s.AddFileSecurity(@"C:\ProgramData\Startup\app.lnk",
                CreateFileSecurity((AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow)));
            s.AddShortcutTarget(@"C:\ProgramData\Startup\app.lnk", @"C:\Tools\app.exe");
            s.AddFileExists(@"C:\Tools\app.exe");
            s.AddFileSecurity(@"C:\Tools\app.exe", fileSec);
            s.AddDirectorySecurity(@"C:\Tools", parentDirSec);
        });

        var results = scanner.RunChecks();

        var autorunFindings = results.Where(f =>
            f is { Category: StartupSecurityCategory.StartupFolder, TargetDescription: @"C:\Tools\app.exe" }).ToList();
        Assert.Single(autorunFindings);
    }

    // ===== Insecure container suppression test =====

    [Fact]
    public void RunChecks_FileInsideInsecureFolder_NotDoubleFlaggedAsAutorun()
    {
        var insecureDirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreatePublicStartupScanner(insecureDirSec, s =>
        {
            s.AddFolderFiles(@"C:\ProgramData\Startup", @"C:\ProgramData\Startup\evil.exe");
            s.AddFileSecurity(@"C:\ProgramData\Startup\evil.exe", fileSec);
            s.AddFileExists(@"C:\ProgramData\Startup\evil.exe");
        });

        var results = scanner.RunChecks();

        // evil.exe inside insecure container: flagged once by file-inside-location check,
        // not again by CheckAutorunExecutables (insecure container suppression).
        var evilExeFindings = results.Where(f =>
            f.TargetDescription.Contains("evil.exe")).ToList();
        Assert.Single(evilExeFindings);
        Assert.Equal(StartupSecurityCategory.StartupFolder, evilExeFindings[0].Category);
    }

    // ===== Wow6432 per-user paths test (validates B2 fix) =====

    // These tests use DefaultScannerDataAccess (real OS APIs) intentionally: the scanner's
    // path-building logic depends on registry hive naming conventions that are best verified
    // against the real implementation rather than a fabricated test double.

    [Fact]
    public void GetWow6432RunKeyPaths_WithUserSid_ReturnsHkuPaths()
    {
        var dataAccess = new DefaultScannerDataAccess();
        var paths = dataAccess.GetWow6432RunKeyPaths("S-1-5-21-123");

        Assert.Equal(4, paths.Count);
        Assert.Contains(paths, p => p.DisplayPath.Contains("HKU") && p.DisplayPath.Contains("Wow6432Node\\Run"));
        Assert.Contains(paths, p => p.DisplayPath.Contains("HKU") && p.DisplayPath.Contains("Wow6432Node\\RunOnce"));
    }

    [Fact]
    public void GetWow6432RunKeyPaths_WithNullUserSid_ReturnsHklmOnly()
    {
        var dataAccess = new DefaultScannerDataAccess();
        var paths = dataAccess.GetWow6432RunKeyPaths(null);

        Assert.Equal(2, paths.Count);
        Assert.All(paths, p => Assert.Contains("HKLM", p.DisplayPath));
    }

    // ===== Unquoted path candidates tests =====

    [Fact]
    public void ComputeUnquotedPathCandidates_UnquotedPath_SpecificCandidates()
    {
        var candidates = CommandLineParser.ComputeUnquotedPathCandidates(@"C:\Program Files\App\svc.exe -arg");

        Assert.Contains(@"C:\Program.exe", candidates);
        // "C:\Program Files\App\svc.exe" already ends with .exe, so no duplicate .exe.exe candidate
        Assert.DoesNotContain(candidates, c => c.EndsWith(".exe.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ComputeUnquotedPathCandidates_MultipleSpaces_ProducesMultipleCandidates()
    {
        var candidates = CommandLineParser.ComputeUnquotedPathCandidates(@"C:\Program Files\My App\svc.exe -arg");

        Assert.Contains(@"C:\Program.exe", candidates);
        Assert.Contains(@"C:\Program Files\My.exe", candidates);
    }

    // ===== NavigationTarget tests =====

    [Fact]
    public void RunChecks_RegistryFinding_HasFullNavigationTarget()
    {
        var regSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow),
            (UserSid1, RegistryRights.SetValue, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.ClearStartupPaths();
            s.AddRegistryKeySecurity(("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), regSec);
        });

        var results = scanner.RunChecks();

        Assert.Single(results);
        Assert.Equal(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            results[0].NavigationTarget);
    }

    [Fact]
    public void RunChecks_FolderFinding_HasPathNavigationTarget()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreatePublicStartupScanner(dirSec);
        var results = scanner.RunChecks();

        Assert.Single(results);
        Assert.Equal(@"C:\ProgramData\Startup", results[0].NavigationTarget);
    }

    // ===== AppInit_DLLs tests =====

    [Fact]
    public void RunChecks_AppInitDlls_WritableKey_Flagged()
    {
        var regSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow),
            (UserSid1, RegistryRights.SetValue, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddAppInitDllEntry(regSec, @"HKLM\...\Windows (AppInit_DLLs)", [@"C:\DLLs\evil.dll"]);
            s.AddFileExists(@"C:\DLLs\evil.dll");
            s.AddFileSecurity(@"C:\DLLs\evil.dll", CreateFileSecurity(
                (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow)));
            s.AddDirectorySecurity(@"C:\DLLs", CreateDirSecurity(
                (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow)));
        });

        var results = scanner.RunChecks();

        var regFindings = results.Where(f =>
            f.Category == StartupSecurityCategory.RegistryRunKey &&
            f.TargetDescription.Contains("AppInit_DLLs")).ToList();
        Assert.Single(regFindings);
        Assert.Equal(UserSid1, regFindings[0].VulnerableSid);
    }

    [Fact]
    public void RunChecks_AppInitDlls_DllPathCollected()
    {
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddAppInitDllEntry(null, @"HKLM\...\Windows (AppInit_DLLs)", [@"C:\DLLs\appinit.dll"]);
            s.AddFileExists(@"C:\DLLs\appinit.dll");
            s.AddFileSecurity(@"C:\DLLs\appinit.dll", fileSec);
            s.AddDirectorySecurity(@"C:\DLLs", CreateDirSecurity(
                (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow)));
        });

        var results = scanner.RunChecks();

        var autorunFindings = results.Where(f =>
            f is { Category: StartupSecurityCategory.RegistryRunKey, TargetDescription: @"C:\DLLs\appinit.dll" }).ToList();
        Assert.Single(autorunFindings);
    }

    // ===== Print Monitor tests =====

    [Fact]
    public void RunChecks_PrintMonitor_WritableKey_Flagged()
    {
        var regSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow),
            (UserSid1, RegistryRights.SetValue, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
            s.AddPrintMonitorEntry(@"HKLM\...\Print\Monitors\Appmon", regSec, [],
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Print\Monitors\Appmon"));

        var results = scanner.RunChecks();

        var monFindings = results.Where(f =>
            f.Category == StartupSecurityCategory.RegistryRunKey &&
            f.TargetDescription.Contains("Appmon")).ToList();
        Assert.Single(monFindings);
        Assert.Equal(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Print\Monitors\Appmon",
            monFindings[0].NavigationTarget);
    }

    // ===== ServiceDll collection tests =====

    [Fact]
    public void RunChecks_ServiceDll_Collected()
    {
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddServiceEntry("MySvc", @"""C:\Windows\System32\svchost.exe -k netsvcs""",
                @"C:\Windows\System32\svchost.exe", @"C:\Windows\System32\mydll.dll");
            s.AddFileExists(@"C:\Windows\System32\mydll.dll");
            s.AddFileSecurity(@"C:\Windows\System32\mydll.dll", fileSec);
            s.AddDirectorySecurity(@"C:\Windows\System32", CreateDirSecurity(
                (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow)));
        });

        var results = scanner.RunChecks();

        var dllFindings = results.Where(f =>
            f is { Category: StartupSecurityCategory.AutoStartService, TargetDescription: @"C:\Windows\System32\mydll.dll" }).ToList();
        Assert.Single(dllFindings);
    }

    // ===== Non-admin owner excludes interactive SID =====

    [Fact]
    public void RunChecks_NonAdminOwner_ExcludesInteractiveSid()
    {
        // When folder owner is NOT an admin, interactive SID should be excluded from per-user folder
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (InteractiveUserSid, FileSystemRights.WriteData, AccessControlType.Allow));

        // InteractiveUserSid is NOT an admin; UserSid2 (owner) is NOT an admin
        var scanner = CreateScanner(s =>
        {
            s.SetAdminMemberSids(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { AdminsSid, CurrentUserSid, "S-1-5-32-544" });
            s.SetPublicStartupPath(null);
            s.SetCurrentUserStartupPath(null);
            s.SetAllUserProfiles([(UserSid2, @"C:\Users\NonAdmin")]);
            s.AddDirectorySecurity(@"C:\Users\NonAdmin\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup", dirSec);
            s.AddFolderFiles(@"C:\Users\NonAdmin\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup");
        });

        var results = scanner.RunChecks();

        // InteractiveUserSid should be excluded because owner (UserSid2) is not an admin
        Assert.Empty(results);
    }

    // ===== Third-party user profile startup folder scanning =====

    [Fact]
    public void RunChecks_ThirdPartyUserProfile_StartupFolderScanned()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.ClearStartupPaths();
            s.SetAllUserProfiles([(UserSid2, @"C:\Users\ThirdParty")]);
            s.AddDirectorySecurity(@"C:\Users\ThirdParty\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup", dirSec);
            s.AddFolderFiles(@"C:\Users\ThirdParty\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup");
        });

        var results = scanner.RunChecks();

        Assert.Single(results);
        Assert.Equal(StartupSecurityCategory.StartupFolder, results[0].Category);
        Assert.Equal(UserSid1, results[0].VulnerableSid);
    }

    // ===== Autorun owner exclusion =====

    [Fact]
    public void RunChecks_AutorunExe_OwnerExcludedForPerUserStartup()
    {
        // Per-user autorun exe in user's startup folder: owner SID excluded from autorun check
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid2, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.ClearStartupPaths();
            s.SetAdminMemberSids(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { AdminsSid, CurrentUserSid, "S-1-5-32-544" });
            s.SetAllUserProfiles([(UserSid2, @"C:\Users\TestUser")]);
            s.AddDirectorySecurity(@"C:\Users\TestUser\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup", dirSec);
            s.AddFolderFiles(@"C:\Users\TestUser\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup",
                @"C:\Users\TestUser\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\myapp.exe");
            s.AddFileExists(@"C:\Users\TestUser\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\myapp.exe");
            s.AddFileSecurity(@"C:\Users\TestUser\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\myapp.exe", fileSec);
        });

        var results = scanner.RunChecks();

        // UserSid2 should be excluded as owner — no autorun findings for their own startup exe
        var autorunFindings = results.Where(f => f.Category == StartupSecurityCategory.StartupFolder &&
                                                 f.TargetDescription.Contains("myapp.exe")).ToList();
        Assert.Empty(autorunFindings);
    }

    [Fact]
    public void RunChecks_AutorunExe_ProfileOwnerExcludedEvenWhenMachineWide()
    {
        // A global task (no per-user principal) references an exe inside a user's profile.
        // The path is marked machine-wide in AutorunContext, but the profile owner should
        // still be excluded from autorun findings via profile-based exclusion.
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid2, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.ClearStartupPaths();
            s.SetAdminMemberSids(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { AdminsSid, CurrentUserSid, "S-1-5-32-544" });
            s.SetAllUserProfiles([(UserSid2, @"C:\Users\TestUser")]);
            // Global task referencing exe in TestUser's profile
            s.SetTaskSchedulerData([
                new ScheduledTaskInfo(@"\GlobalUpdater", @"\", "GlobalUpdater",
                    [@"C:\Users\TestUser\AppData\Local\app\updater.exe"],
                    false, null) // not per-user — global task
            ]);
            s.AddFileExists(@"C:\Users\TestUser\AppData\Local\app\updater.exe");
            s.AddFileSecurity(@"C:\Users\TestUser\AppData\Local\app\updater.exe", fileSec);
            s.AddDirectorySecurity(@"C:\Users\TestUser\AppData\Local\app", dirSec);
        });

        var results = scanner.RunChecks();

        // UserSid2 should be excluded via profile-based exclusion even though
        // the path was added machine-wide (no per-user task owner exclusion)
        var autorunFindings = results.Where(f =>
            f is { Category: StartupSecurityCategory.TaskScheduler, VulnerableSid: UserSid2 }).ToList();
        Assert.Empty(autorunFindings);
    }

    // ===== desktop.ini NOT collected as autorun =====

    [Fact]
    public void RunChecks_DesktopIni_NotCollectedAsAutorun()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreatePublicStartupScanner(dirSec, s =>
        {
            s.AddFolderFiles(@"C:\ProgramData\Startup", @"C:\ProgramData\Startup\desktop.ini");
            s.AddFileSecurity(@"C:\ProgramData\Startup\desktop.ini", fileSec);
            s.AddFileExists(@"C:\ProgramData\Startup\desktop.ini");
        });

        var results = scanner.RunChecks();

        // desktop.ini should NOT appear in ANY category — not as autorun exe nor as StartupFolder file
        var desktopIniFindings = results.Where(f =>
            f.TargetDescription.Contains("desktop.ini", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Empty(desktopIniFindings);
    }

    [Fact]
    public void RunChecks_NonStandardExtensionInStartupFolder_StillFlagged()
    {
        // Denylist inversion: unknown extensions like .ahk ARE checked (fails open).
        // Only known-inert files (desktop.ini, .txt, images, etc.) are skipped.
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreatePublicStartupScanner(dirSec, s =>
        {
            s.AddFolderFiles(@"C:\ProgramData\Startup", @"C:\ProgramData\Startup\script.ahk");
            s.AddFileSecurity(@"C:\ProgramData\Startup\script.ahk", fileSec);
            s.AddFileExists(@"C:\ProgramData\Startup\script.ahk");
        });

        var results = scanner.RunChecks();

        // .ahk should be flagged as a StartupFolder file (denylist lets it through)
        var startupFindings = results.Where(f =>
            f.Category == StartupSecurityCategory.StartupFolder &&
            f.TargetDescription.Contains("script.ahk", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(startupFindings);
        Assert.Equal(UserSid1, startupFindings[0].VulnerableSid);
    }

    // ===== T1: Smoke tests for untested scanner areas =====

    [Fact]
    public void RunChecks_LsaPackageWritableKey_Flagged()
    {
        var regSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow),
            (UserSid1, RegistryRights.SetValue, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s => s.AddLsaPackageEntry(regSec, []));

        var results = scanner.RunChecks();

        Assert.Single(results);
        Assert.Equal(StartupSecurityCategory.RegistryRunKey, results[0].Category);
        Assert.Equal(UserSid1, results[0].VulnerableSid);
    }

    [Fact]
    public void RunChecks_NetworkProviderWritableKey_Flagged()
    {
        var regSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow),
            (UserSid1, RegistryRights.SetValue, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
            s.AddNetworkProviderEntry(@"HKLM\...\NetworkProvider\TestProv", regSec, [],
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\NetworkProvider\Order"));

        var results = scanner.RunChecks();

        Assert.Single(results);
        Assert.Equal(StartupSecurityCategory.RegistryRunKey, results[0].Category);
        Assert.Equal(UserSid1, results[0].VulnerableSid);
    }

    [Fact]
    public void RunChecks_SharedWrapperScriptsWritableFolder_Flagged()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetSharedWrapperScriptsDir(@"C:\ProgramData\WrapperScripts");
            s.AddDirectorySecurity(@"C:\ProgramData\WrapperScripts", dirSec);
            s.AddFolderFiles(@"C:\ProgramData\WrapperScripts");
        });

        var results = scanner.RunChecks();

        Assert.Single(results);
        Assert.Equal(StartupSecurityCategory.LogonScript, results[0].Category);
        Assert.Contains("WrapperScripts", results[0].TargetDescription);
    }

    [Fact]
    public void RunChecks_PerUserLogonScriptsWritableFolder_Flagged()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.ClearStartupPaths();
            s.SetAllUserProfiles([(UserSid2, @"C:\Users\OtherUser")]);
            // Set up the GP scripts directory for the user — this is what the scanner checks
            s.SetGpScriptsDir(UserSid2, @"C:\Windows\System32\GroupPolicy\User\Scripts");
            s.AddDirectorySecurity(@"C:\Windows\System32\GroupPolicy\User\Scripts", dirSec);
            s.AddFolderFiles(@"C:\Windows\System32\GroupPolicy\User\Scripts");
        });

        var results = scanner.RunChecks();

        var logonFindings = results.Where(f =>
            f.Category == StartupSecurityCategory.LogonScript).ToList();
        Assert.NotEmpty(logonFindings);
    }

    // --- Account Lockout Policy ---

    [Fact]
    public void AccountLockoutDisabled_ReportsSingleMachineFinding()
    {
        var scanner = CreateIsolatedScanner(s => { s.SetAccountLockoutThreshold(0); });

        var results = scanner.RunChecks();

        var policyFindings = results.Where(f => f.Category == StartupSecurityCategory.AccountPolicy).ToList();
        Assert.Single(policyFindings);
        Assert.Equal("", policyFindings[0].VulnerableSid);
        Assert.Equal("All local accounts", policyFindings[0].VulnerablePrincipal);
        Assert.Equal("secpol.msc", policyFindings[0].NavigationTarget);
    }

    [Fact]
    public void AccountLockoutDisabled_AllAdminAccounts_StillReportsFinding()
    {
        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetAccountLockoutThreshold(0);
            s.SetAllUserProfiles([(AdminUserSid, @"C:\Users\Admin")]);
            s.SetAdminMemberSids(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                AdminsSid, AdminUserSid, "S-1-5-32-544"
            });
        });

        var results = scanner.RunChecks();

        Assert.Single(results, f => f.Category == StartupSecurityCategory.AccountPolicy);
    }

    [Fact]
    public void AccountLockoutEnabled_NoFindings()
    {
        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetAccountLockoutThreshold(5);
            s.SetAllUserProfiles([(UserSid1, @"C:\Users\Alice")]);
        });

        var results = scanner.RunChecks();

        Assert.DoesNotContain(results, f => f.Category == StartupSecurityCategory.AccountPolicy);
    }

    [Fact]
    public void AccountLockoutUnavailable_NoFindings()
    {
        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetAccountLockoutThreshold(null);
            s.SetAllUserProfiles([(UserSid1, @"C:\Users\Alice")]);
        });

        var results = scanner.RunChecks();

        Assert.DoesNotContain(results, f => f.Category == StartupSecurityCategory.AccountPolicy);
    }

    [Fact]
    public void AccountLockoutEnabled_AdminLockoutDisabled_ReportsFinding()
    {
        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetAccountLockoutThreshold(5);
            s.SetAdminAccountLockoutEnabled(false);
        });

        var results = scanner.RunChecks();

        var policyFindings = results.Where(f => f.Category == StartupSecurityCategory.AccountPolicy).ToList();
        Assert.Single(policyFindings);
        Assert.Equal("", policyFindings[0].VulnerableSid);
        Assert.Equal("Administrator accounts", policyFindings[0].VulnerablePrincipal);
        Assert.Equal("secpol.msc", policyFindings[0].NavigationTarget);
    }

    [Fact]
    public void AccountLockoutEnabled_AdminLockoutEnabled_NoFindings()
    {
        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetAccountLockoutThreshold(5);
            s.SetAdminAccountLockoutEnabled(true);
        });

        var results = scanner.RunChecks();

        Assert.DoesNotContain(results, f => f.Category == StartupSecurityCategory.AccountPolicy);
    }

    [Fact]
    public void AccountLockoutEnabled_AdminLockoutUnavailable_NoFindings()
    {
        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetAccountLockoutThreshold(5);
            s.SetAdminAccountLockoutEnabled(null);
        });

        var results = scanner.RunChecks();

        Assert.DoesNotContain(results, f => f.Category == StartupSecurityCategory.AccountPolicy);
    }

    [Fact]
    public void AccountLockoutDisabled_AdminLockoutAlsoDisabled_ReportsOnlyThresholdFinding()
    {
        // When threshold = 0, the "no lockout at all" finding takes priority;
        // admin-exemption finding would be redundant and is not reported.
        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetAccountLockoutThreshold(0);
            s.SetAdminAccountLockoutEnabled(false);
        });

        var results = scanner.RunChecks();

        var policyFindings = results.Where(f => f.Category == StartupSecurityCategory.AccountPolicy).ToList();
        Assert.Single(policyFindings);
        Assert.Equal("All local accounts", policyFindings[0].VulnerablePrincipal);
    }

    // --- Blank Password Policy ---

    [Fact]
    public void BlankPasswordRestrictionDisabled_ReportsFinding()
    {
        var scanner = CreateIsolatedScanner(s => { s.SetBlankPasswordRestrictionEnabled(false); });

        var results = scanner.RunChecks();

        var policyFindings = results.Where(f => f.Category == StartupSecurityCategory.AccountPolicy).ToList();
        Assert.Single(policyFindings);
        Assert.Contains("LimitBlankPasswordUse", policyFindings[0].TargetDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("", policyFindings[0].VulnerableSid);
        Assert.Equal("secpol.msc", policyFindings[0].NavigationTarget);
    }

    [Fact]
    public void BlankPasswordRestrictionEnabled_NoFinding()
    {
        var scanner = CreateIsolatedScanner(s => { s.SetBlankPasswordRestrictionEnabled(true); });

        var results = scanner.RunChecks();

        Assert.DoesNotContain(results, f => f.Category == StartupSecurityCategory.AccountPolicy);
    }

    [Fact]
    public void BlankPasswordRestrictionUnavailable_NoFinding()
    {
        var scanner = CreateIsolatedScanner(s => { s.SetBlankPasswordRestrictionEnabled(null); });

        var results = scanner.RunChecks();

        Assert.DoesNotContain(results, f => f.Category == StartupSecurityCategory.AccountPolicy);
    }

    // --- Windows Firewall ---

    [Fact]
    public void FirewallServiceDisabled_ReportsSingleFinding_SkipsProfileCheck()
    {
        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetWindowsFirewallServiceState((IsDisabled: true, IsStopped: true));
            s.SetFirewallProfileStates([("Public", false)]);
        });

        var results = scanner.RunChecks();

        var firewallFindings = results.Where(f => f.Category == StartupSecurityCategory.FirewallPolicy).ToList();
        Assert.Single(firewallFindings);
        Assert.Contains("disabled", firewallFindings[0].TargetDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("services.msc", firewallFindings[0].NavigationTarget);
    }

    [Fact]
    public void FirewallServiceStopped_NotDisabled_ReportsSingleFinding_SkipsProfileCheck()
    {
        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetWindowsFirewallServiceState((IsDisabled: false, IsStopped: true));
            s.SetFirewallProfileStates([("Public", false)]);
        });

        var results = scanner.RunChecks();

        var firewallFindings = results.Where(f => f.Category == StartupSecurityCategory.FirewallPolicy).ToList();
        Assert.Single(firewallFindings);
        Assert.Contains("not running", firewallFindings[0].TargetDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("services.msc", firewallFindings[0].NavigationTarget);
    }

    [Fact]
    public void FirewallServiceRunning_ProfileDisabled_ReportsProfileFinding()
    {
        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetWindowsFirewallServiceState((IsDisabled: false, IsStopped: false));
            s.SetFirewallProfileStates([
                ("Domain", true),
                ("Private", false),
                ("Public", false),
            ]);
        });

        var results = scanner.RunChecks();

        var firewallFindings = results.Where(f => f.Category == StartupSecurityCategory.FirewallPolicy).ToList();
        Assert.Equal(2, firewallFindings.Count);
        Assert.Contains(firewallFindings, f => f.TargetDescription.Contains("Private"));
        Assert.Contains(firewallFindings, f => f.TargetDescription.Contains("Public"));
        Assert.All(firewallFindings, f => Assert.Equal("wf.msc", f.NavigationTarget));
    }

    [Fact]
    public void FirewallServiceRunning_AllProfilesEnabled_NoFindings()
    {
        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetWindowsFirewallServiceState((IsDisabled: false, IsStopped: false));
            s.SetFirewallProfileStates([("Domain", true), ("Private", true), ("Public", true)]);
        });

        var results = scanner.RunChecks();

        Assert.DoesNotContain(results, f => f.Category == StartupSecurityCategory.FirewallPolicy);
    }

    [Fact]
    public void FirewallServiceStateUnavailable_ProfileDisabled_ReportsProfileFinding()
    {
        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetWindowsFirewallServiceState(null);
            s.SetFirewallProfileStates([("Public", false)]);
        });

        var results = scanner.RunChecks();

        var firewallFindings = results.Where(f => f.Category == StartupSecurityCategory.FirewallPolicy).ToList();
        Assert.Single(firewallFindings);
        Assert.Contains("Public", firewallFindings[0].TargetDescription);
        Assert.Equal("wf.msc", firewallFindings[0].NavigationTarget);
    }

    [Fact]
    public void FirewallBothUnavailable_NoFindings()
    {
        var scanner = CreateIsolatedScanner(s =>
        {
            s.SetWindowsFirewallServiceState(null);
            s.SetFirewallProfileStates(null);
        });

        var results = scanner.RunChecks();

        Assert.DoesNotContain(results, f => f.Category == StartupSecurityCategory.FirewallPolicy);
    }

    // ===== DiskRootScanner tests =====

    [Fact]
    public void DiskRoot_WritableByNonAdmin_Flagged()
    {
        // A disk root where a non-admin user has write rights must be flagged as DiskRootAcl.
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddDriveRoot(@"C:\");
            s.AddDirectorySecurity(@"C:\", dirSec);
        });

        var results = scanner.RunChecks();

        Assert.Single(results);
        Assert.Equal(StartupSecurityCategory.DiskRootAcl, results[0].Category);
        Assert.Equal(@"C:\", results[0].TargetDescription);
        Assert.Equal(UserSid1, results[0].VulnerableSid);
    }

    [Fact]
    public void DiskRoot_AdminOnlyAccess_NotFlagged()
    {
        // A disk root where only admins have write rights must not be flagged.
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddDriveRoot(@"C:\");
            s.AddDirectorySecurity(@"C:\", dirSec);
        });

        var results = scanner.RunChecks();

        Assert.Empty(results);
    }

    [Fact]
    public void DiskRoot_AccessErrorGettingSecurity_SkippedSilently()
    {
        // GetDirectorySecurity throws (access denied) — the root must be skipped without crash or finding.
        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddDriveRoot(@"D:\");
            s.AddDirSecurityThrows(@"D:\");
        });

        var results = scanner.RunChecks();

        Assert.DoesNotContain(results, f => f.Category == StartupSecurityCategory.DiskRootAcl);
    }

    [Fact]
    public void DiskRoot_MultipleRoots_EachCheckedIndependently()
    {
        // C:\ is admin-only (not flagged), D:\ has non-admin write (flagged).
        var adminOnlySec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));
        var writableSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid2, FileSystemRights.AppendData, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddDriveRoot(@"C:\");
            s.AddDriveRoot(@"D:\");
            s.AddDirectorySecurity(@"C:\", adminOnlySec);
            s.AddDirectorySecurity(@"D:\", writableSec);
        });

        var results = scanner.RunChecks();

        var diskRootFindings = results.Where(f => f.Category == StartupSecurityCategory.DiskRootAcl).ToList();
        Assert.Single(diskRootFindings);
        Assert.Equal(@"D:\", diskRootFindings[0].TargetDescription);
        Assert.Equal(UserSid2, diskRootFindings[0].VulnerableSid);
    }

    [Fact]
    public void RunChecks_IfeoWow6432WritableKey_Flagged()
    {
        // The Wow6432 IFEO key is a separate registry key checked alongside the main IFEO key.
        // A non-admin with CreateSubKey on this key can insert a debugger entry targeting any 32-bit exe.
        var regSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow),
            (UserSid1, RegistryRights.CreateSubKey, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s => s.SetIfeoWow6432RegistryKeySecurity(regSec));

        var results = scanner.RunChecks();

        var ifeoFindings = results.Where(f =>
            f.Category == StartupSecurityCategory.RegistryRunKey &&
            f.TargetDescription.Contains("Image File Execution Options")).ToList();
        Assert.Single(ifeoFindings);
        Assert.Equal(UserSid1, ifeoFindings[0].VulnerableSid);
        Assert.StartsWith("HKEY_LOCAL_MACHINE", ifeoFindings[0].NavigationTarget);
    }

    [Fact]
    public void RunChecks_IfeoSubkeyVerifierDlls_CollectedAsAutorun()
    {
        // VerifierDlls value under an IFEO subkey is collected as an autorun path.
        // A writable verifier DLL is flagged as an autorun finding.
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));
        var parentDirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddIfeoSubkeyName("notepad.exe");
            s.SetIfeoVerifierDlls("notepad.exe", @"C:\Tools\verifier.dll");
            s.AddFileExists(@"C:\Tools\verifier.dll");
            s.AddFileSecurity(@"C:\Tools\verifier.dll", fileSec);
            s.AddDirectorySecurity(@"C:\Tools", parentDirSec);
        });

        var results = scanner.RunChecks();

        var verifierFindings = results.Where(f =>
            f is { Category: StartupSecurityCategory.RegistryRunKey, TargetDescription: @"C:\Tools\verifier.dll" }).ToList();
        Assert.Single(verifierFindings);
        Assert.Equal(UserSid1, verifierFindings[0].VulnerableSid);
    }

    // --- Test IScannerDataAccess implementation ---

    public sealed class TestScannerDataAccess : IScannerDataAccess
    {
        private string? _publicStartupPath;
        private string? _currentUserStartupPath;
        private string? _currentUserSid;
        private string? _interactiveUserSid;
        private string? _interactiveProfilePath;
        private bool _interactiveProfilePathSet;
        private HashSet<string> _adminMemberSids = new(StringComparer.OrdinalIgnoreCase);
        private RegistrySecurity? _winlogonKeySecurity;
        private readonly List<string> _winlogonExePaths = [];
        private RegistrySecurity? _ifeoKeySecurity;
        private List<ScheduledTaskInfo>? _taskSchedulerData;
        private List<(string SubKeyPath, string DisplayPath)>? _wow6432RunKeyPaths;
        private string _machineGpScriptsDir = "";
        private List<string>? _machineGpScriptPaths;
        private readonly List<AppInitDllEntry> _appInitDllEntries = [];
        private readonly List<RegistryDllEntry> _printMonitorEntries = [];
        private readonly List<(RegistrySecurity? Security, List<string> DllPaths)> _lsaPackageEntries = [];
        private readonly List<RegistryDllEntry> _networkProviderEntries = [];
        private string _sharedWrapperScriptsDir = "";
        private readonly Dictionary<string, List<string>> _logonScriptPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _ifeoSubkeyNames = [];
        private readonly Dictionary<string, string?> _ifeoDebuggerPaths = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, DirectorySecurity> _dirSecurities = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FileSecurity> _fileSecurities = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<(string Hive, string Path), RegistrySecurity> _regSecurities =
            new(CaseInsensitiveTupleComparer.Instance);

        private readonly Dictionary<string, string[]> _folderFiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _dirSecurityThrows = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _fileSecurityThrows = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _existingFiles = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<(string Hive, string Path), List<string>> _registryAutorunPaths =
            new(CaseInsensitiveTupleComparer.Instance);

        private readonly Dictionary<string, string> _shortcutTargets = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RegistrySecurity> _serviceRegSecurities = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string Name, string ImagePath, string Expanded, string? Dll)> _services = [];
        private List<(string Sid, string? ProfilePath)>? _allUserProfiles;
        private readonly Dictionary<string, HashSet<string>?> _groupMembers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _gpScriptsDirs = new(StringComparer.OrdinalIgnoreCase);

        // --- Public setters for test configuration ---

        public void SetGroupMemberSids(string groupSid, HashSet<string>? memberSids) =>
            _groupMembers[groupSid] = memberSids;

        public void SetPublicStartupPath(string? path) => _publicStartupPath = path;
        public void SetCurrentUserStartupPath(string? path) => _currentUserStartupPath = path;

        /// <summary>Nulls out startup paths and interactive user SID to isolate non-startup tests.</summary>
        public void ClearStartupPaths()
        {
            _publicStartupPath = null;
            _currentUserStartupPath = null;
            _interactiveUserSid = null;
        }

        public void SetCurrentUserSid(string? sid) => _currentUserSid = sid;
        public void SetInteractiveUserSid(string? sid) => _interactiveUserSid = sid;
        public void SetAdminMemberSids(HashSet<string> sids) => _adminMemberSids = sids;

        public void SetInteractiveProfilePath(string? path)
        {
            _interactiveProfilePath = path;
            _interactiveProfilePathSet = true;
        }

        public void SetTaskSchedulerData(List<ScheduledTaskInfo> data) => _taskSchedulerData = data;
        public void SetWow6432RunKeyPaths(List<(string SubKeyPath, string DisplayPath)> paths) => _wow6432RunKeyPaths = paths;
        public void SetMachineGpScriptsDir(string dir) => _machineGpScriptsDir = dir;
        public void SetMachineGpScriptPaths(List<string> paths) => _machineGpScriptPaths = paths;

        public void AddDirectorySecurity(string path, DirectorySecurity security) =>
            _dirSecurities[path] = security;

        public void AddFileSecurity(string path, FileSecurity security) =>
            _fileSecurities[path] = security;

        public void AddRegistryKeySecurity((string Hive, string Path) key, RegistrySecurity security) =>
            _regSecurities[key] = security;

        public void AddFolderFiles(string folderPath, params string[] files) =>
            _folderFiles[folderPath] = files;

        public void AddFileExists(string path) => _existingFiles.Add(path);

        public void AddRegistryAutorunPaths((string Hive, string Path) key, List<string> paths) =>
            _registryAutorunPaths[key] = paths;

        public void AddShortcutTarget(string lnkPath, string targetPath) =>
            _shortcutTargets[lnkPath] = targetPath;

        public void AddServiceEntry(string name, string imagePath, string expanded, string? dll = null) =>
            _services.Add((name, imagePath, expanded, dll));

        public void AddServiceRegistryKeySecurity(string name, RegistrySecurity security) =>
            _serviceRegSecurities[name] = security;

        public void SetWinlogonRegistryKeySecurity(RegistrySecurity security) =>
            _winlogonKeySecurity = security;

        public void SetIfeoRegistryKeySecurity(RegistrySecurity security) =>
            _ifeoKeySecurity = security;

        public void AddAppInitDllEntry(RegistrySecurity? security, string displayPath, List<string> dllPaths) =>
            _appInitDllEntries.Add(new AppInitDllEntry(security, displayPath, dllPaths));

        public void AddPrintMonitorEntry(string displayPath, RegistrySecurity? security, List<string> dllPaths, string navTarget) =>
            _printMonitorEntries.Add(new RegistryDllEntry(displayPath, security, dllPaths, navTarget));

        public void AddLsaPackageEntry(RegistrySecurity? security, List<string> dllPaths) =>
            _lsaPackageEntries.Add((security, dllPaths));

        public void AddNetworkProviderEntry(string displayPath, RegistrySecurity? security, List<string> dllPaths, string navTarget) =>
            _networkProviderEntries.Add(new RegistryDllEntry(displayPath, security, dllPaths, navTarget));

        public void SetSharedWrapperScriptsDir(string dir) => _sharedWrapperScriptsDir = dir;

        public void AddLogonScriptPaths(string userSid, List<string> paths) =>
            _logonScriptPaths[userSid] = paths;

        public void SetGpScriptsDir(string userSid, string dir) => _gpScriptsDirs[userSid] = dir;
        public void AddIfeoSubkeyName(string name) => _ifeoSubkeyNames.Add(name);
        public void SetIfeoDebuggerPath(string exeName, string? path) => _ifeoDebuggerPaths[exeName] = path;
        public void AddFileSecurityThrows(string path) => _fileSecurityThrows.Add(path);
        public void SetAllUserProfiles(List<(string Sid, string? ProfilePath)> profiles) => _allUserProfiles = profiles;

        private int? _accountLockoutThreshold;
        public void SetAccountLockoutThreshold(int? threshold) => _accountLockoutThreshold = threshold;

        private bool? _adminAccountLockoutEnabled = true;
        public void SetAdminAccountLockoutEnabled(bool? value) => _adminAccountLockoutEnabled = value;

        private bool? _blankPasswordRestrictionEnabled = true;
        public void SetBlankPasswordRestrictionEnabled(bool? value) => _blankPasswordRestrictionEnabled = value;

        private List<(string ProfileName, bool Enabled)>? _firewallProfileStates;

        public void SetFirewallProfileStates(List<(string ProfileName, bool Enabled)>? states) =>
            _firewallProfileStates = states;

        private (bool IsDisabled, bool IsStopped)? _firewallServiceState;

        public void SetWindowsFirewallServiceState((bool IsDisabled, bool IsStopped)? state) =>
            _firewallServiceState = state;

        // --- IScannerDataAccess implementation ---

        public string? GetPublicStartupPath() => _publicStartupPath;
        public string? GetCurrentUserStartupPath() => _currentUserStartupPath;
        public string? GetCurrentUserSid() => _currentUserSid;
        public string? GetInteractiveUserSid() => _interactiveUserSid;
        public HashSet<string> GetAdminMemberSids() => _adminMemberSids;

        public string? GetInteractiveUserProfilePath(string sid)
        {
            if (_interactiveProfilePathSet)
                return _interactiveProfilePath;
            return null;
        }

        public List<(string Sid, string? ProfilePath)> GetAllLocalUserProfiles() => _allUserProfiles ?? [];

        public HashSet<string>? TryGetGroupMemberSids(string groupSid) =>
            _groupMembers.GetValueOrDefault(groupSid);

        public Dictionary<string, HashSet<string>?> BulkLookupGroupMemberSids(IReadOnlyList<string> sids)
        {
            var result = new Dictionary<string, HashSet<string>?>(StringComparer.OrdinalIgnoreCase);
            foreach (var sid in sids)
                result[sid] = _groupMembers.GetValueOrDefault(sid);
            return result;
        }

        public List<ScheduledTaskInfo> GetTaskSchedulerData() => _taskSchedulerData ?? [];

        public List<string> GetLogonScriptPaths(string userSid) =>
            _logonScriptPaths.TryGetValue(userSid, out var paths) ? paths : [];

        public string GetGpScriptsDir(string userSid) =>
            _gpScriptsDirs.GetValueOrDefault(userSid, "");

        public string GetMachineGpScriptsDir() => _machineGpScriptsDir;
        public List<string> GetMachineGpScriptPaths() => _machineGpScriptPaths ?? [];
        public string GetSharedWrapperScriptsDir() => _sharedWrapperScriptsDir;

        public List<(string SubKeyPath, string DisplayPath)> GetWow6432RunKeyPaths(string? userSid)
        {
            if (_wow6432RunKeyPaths != null)
                return _wow6432RunKeyPaths;
            // Default: same logic as DefaultScannerDataAccess
            var paths = new List<(string, string)>
            {
                (@"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run", @"HKLM\...\Wow6432Node\Run"),
                (@"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\RunOnce", @"HKLM\...\Wow6432Node\RunOnce")
            };
            if (userSid != null)
            {
                paths.Add(($@"{userSid}\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run", $@"HKU\{userSid}\...\Wow6432Node\Run"));
                paths.Add(($@"{userSid}\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\RunOnce", $@"HKU\{userSid}\...\Wow6432Node\RunOnce"));
            }

            return paths;
        }

        public List<string> GetIfeoSubkeyNames() => _ifeoSubkeyNames;

        public string? GetIfeoDebuggerPath(string exeName) =>
            _ifeoDebuggerPaths.GetValueOrDefault(exeName);

        private readonly Dictionary<string, string?> _ifeoVerifierDlls = new(StringComparer.OrdinalIgnoreCase);
        public void SetIfeoVerifierDlls(string exeName, string? dlls) => _ifeoVerifierDlls[exeName] = dlls;
        public string? GetIfeoVerifierDlls(string exeName) => _ifeoVerifierDlls.GetValueOrDefault(exeName);

        public List<AppInitDllEntry> GetAppInitDllEntries() =>
            _appInitDllEntries.Count > 0 ? _appInitDllEntries : [];

        public List<RegistryDllEntry> GetPrintMonitorEntries() =>
            _printMonitorEntries.Count > 0 ? _printMonitorEntries : [];

        public List<(RegistrySecurity? Security, List<string> DllPaths)> GetLsaPackageEntries() =>
            _lsaPackageEntries.Count > 0 ? _lsaPackageEntries : [];

        public List<RegistryDllEntry> GetNetworkProviderEntries() =>
            _networkProviderEntries.Count > 0 ? _networkProviderEntries : [];

        public string GetMachineGpUserScriptsDir() => "";

        public List<ServiceInfo> GetAutoStartServices() =>
            _services.Select(s => new ServiceInfo(s.Name, s.ImagePath, s.Expanded, s.Dll)).ToList();

        public RegistrySecurity? GetServiceRegistryKeySecurity(string serviceName) =>
            _serviceRegSecurities.GetValueOrDefault(serviceName);

        public RegistrySecurity? GetWinlogonRegistryKeySecurity() => _winlogonKeySecurity;
        public List<string> GetWinlogonExePaths() => _winlogonExePaths;
        public RegistrySecurity? GetIfeoRegistryKeySecurity() => _ifeoKeySecurity;
        private RegistrySecurity? _ifeoWow6432KeySecurity;
        public void SetIfeoWow6432RegistryKeySecurity(RegistrySecurity security) => _ifeoWow6432KeySecurity = security;
        public RegistrySecurity? GetIfeoWow6432RegistryKeySecurity() => _ifeoWow6432KeySecurity;

        public bool DirectoryExists(string path) =>
            _dirSecurities.ContainsKey(path) || _folderFiles.ContainsKey(path) || _dirSecurityThrows.Contains(path);

        public bool FileExists(string path) => _existingFiles.Contains(path);

        public DirectorySecurity GetDirectorySecurity(string path)
        {
            if (_dirSecurityThrows.Contains(path))
                throw new UnauthorizedAccessException("Access denied (test)");
            if (_dirSecurities.TryGetValue(path, out var sec))
                return sec;
            throw new DirectoryNotFoundException($"Not found: {path}");
        }

        public FileSecurity GetFileSecurity(string path)
        {
            if (_fileSecurityThrows.Contains(path))
                throw new UnauthorizedAccessException("Access denied (test)");
            if (_fileSecurities.TryGetValue(path, out var sec))
                return sec;
            throw new FileNotFoundException($"Not found: {path}");
        }

        public RegistrySecurity? GetRegistryKeySecurity(RegistryKey hive, string subKeyPath)
        {
            var hiveName = GetHiveName(hive);
            var key = (hiveName, subKeyPath);
            return _regSecurities.GetValueOrDefault(key);
        }

        public string[] GetFilesInFolder(string folderPath)
        {
            if (_folderFiles.TryGetValue(folderPath, out var files))
                return files;
            return [];
        }

        public IEnumerable<string> GetDriveRoots() => _driveRoots;

        private readonly List<string> _driveRoots = [];
        public void AddDriveRoot(string root) => _driveRoots.Add(root);

        public List<string> GetRegistryAutorunPaths(RegistryKey hive, string subKeyPath)
        {
            var hiveName = GetHiveName(hive);
            var key = (hiveName, subKeyPath);
            if (_registryAutorunPaths.TryGetValue(key, out var paths))
                return paths;
            return [];
        }

        public string? ResolveShortcutTarget(string lnkPath)
        {
            return _shortcutTargets.GetValueOrDefault(lnkPath);
        }

        public string ResolveDisplayName(string sidString) => sidString;

        public int? GetAccountLockoutThreshold() => _accountLockoutThreshold;
        public bool? GetAdminAccountLockoutEnabled() => _adminAccountLockoutEnabled;
        public bool? GetBlankPasswordRestrictionEnabled() => _blankPasswordRestrictionEnabled;
        public List<(string ProfileName, bool Enabled)>? GetFirewallProfileStates() => _firewallProfileStates;
        public (bool IsDisabled, bool IsStopped)? GetWindowsFirewallServiceState() => _firewallServiceState;

        public void LogError(string message)
        {
            /* suppress in tests */
        }

        public void AddDirSecurityThrows(string path) => _dirSecurityThrows.Add(path);

        private static string GetHiveName(RegistryKey hive)
        {
            if (hive == Registry.LocalMachine)
                return "HKLM";
            if (hive == Registry.CurrentUser)
                return "HKCU";
            if (hive == Registry.Users)
                return "HKU";
            return hive.Name;
        }
    }
}