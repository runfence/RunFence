using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core.Models;
using RunFence.SecurityScanner;
using Xunit;

namespace RunFence.Tests;
public sealed class SecurityScannerAutorunAndIfeoTests : SecurityScannerTestBase
{
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
        // Only the direct write finding should appear â€” replacement is redundant.
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

        // No replacement finding â€” exe is inside autorun location
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
        // Group whose only member is an admin â†’ should not be flagged
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
        // Group has a member not in exclusions â€” should be flagged
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
        // Group with empty member list â€” member enumeration may have failed; do not suppress
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
        // SID that can't be resolved as a group â€” should be flagged normally
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.SetPublicStartupPath(@"C:\ProgramData\Startup");
            s.AddDirectorySecurity(@"C:\ProgramData\Startup", dirSec);
            // No SetGroupMemberSids for UserSid1 â†’ TryGetGroupMemberSids returns null
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
        // Group whose only member is a trusted system SID â€” should be suppressed
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
        // Member individually has write rights â†’ group is redundant (suppressed), user individually reported
        yield return [FileSystemRights.WriteData, false, true];
        // Member individually has read-only rights â†’ group NOT suppressed (covers write not in individual ACE)
        yield return [FileSystemRights.ReadAndExecute, true, false];
        // Member has no individual ACE â†’ group NOT suppressed (only path to write access)
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

    [Fact]
    public void RunChecks_ServiceParametersKeyWritable_Flagged()
    {
        var regSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow),
            (UserSid1, RegistryRights.SetValue, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddServiceEntry("ParamSvc", @"""C:\Svc\paramsvc.exe""", @"C:\Svc\paramsvc.exe");
            s.AddServiceParametersRegistryKeySecurity("ParamSvc", regSec);
        });

        var results = scanner.RunChecks();

        Assert.Contains(results, f =>
            f is { Category: StartupSecurityCategory.AutoStartService, VulnerableSid: UserSid1 } &&
            f.TargetDescription.Contains(@"ParamSvc\Parameters", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RunChecks_ServiceParametersKeyWritableByNetworkConfigurationOperators_NotFlagged()
    {
        var regSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow),
            (NetworkConfigurationOperatorsSid, RegistryRights.SetValue, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddServiceEntry("ParamSvc", @"""C:\Svc\paramsvc.exe""", @"C:\Svc\paramsvc.exe");
            s.AddServiceParametersRegistryKeySecurity("ParamSvc", regSec);
        });

        var results = scanner.RunChecks();

        Assert.DoesNotContain(results, f =>
            f is { Category: StartupSecurityCategory.AutoStartService, VulnerableSid: NetworkConfigurationOperatorsSid } &&
            f.TargetDescription.Contains(@"ParamSvc\Parameters", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RunChecks_ServiceKeyWritableByNetworkConfigurationOperators_Flagged()
    {
        var regSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow),
            (NetworkConfigurationOperatorsSid, RegistryRights.SetValue, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddServiceEntry("ParamSvc", @"""C:\Svc\paramsvc.exe""", @"C:\Svc\paramsvc.exe");
            s.AddServiceRegistryKeySecurity("ParamSvc", regSec);
        });

        var results = scanner.RunChecks();

        Assert.Contains(results, f =>
            f is { Category: StartupSecurityCategory.AutoStartService, VulnerableSid: NetworkConfigurationOperatorsSid } &&
            !f.TargetDescription.Contains(@"\Parameters", StringComparison.OrdinalIgnoreCase) &&
            f.TargetDescription.Contains(@"HKLM\...\Services\ParamSvc", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RunChecks_AutoStartDriverImage_Collected()
    {
        var driverPath = @"C:\Windows\System32\drivers\vuln.sys";
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddServiceEntry("DriverSvc", @"\SystemRoot\System32\drivers\vuln.sys", driverPath);
            s.AddFileExists(driverPath);
            s.AddFileSecurity(driverPath, fileSec);
            s.AddDirectorySecurity(@"C:\Windows\System32\drivers", dirSec);
        });

        var results = scanner.RunChecks();

        Assert.Contains(results, f =>
            f is { Category: StartupSecurityCategory.AutoStartService, TargetDescription: @"C:\Windows\System32\drivers\vuln.sys", VulnerableSid: UserSid1 });
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
    // cmd /c "start [/B] path" â€” quoted inner command with start
    [InlineData("cmd /c \"start /B C:\\tools\\app.exe\"", "C:\\tools\\app.exe")]
    [InlineData("cmd /c \"start C:\\tools\\app.exe\"", "C:\\tools\\app.exe")]
    [InlineData("cmd.exe /c \"start /B C:\\tools\\app.exe\"", "C:\\tools\\app.exe")]
    // cmd /c "[path]" â€” quoted path directly
    [InlineData("cmd /c \"C:\\tools\\script.bat\"", "C:\\tools\\script.bat")]
    [InlineData("C:\\Windows\\System32\\cmd.exe /c \"C:\\tools\\script.bat\"", "C:\\tools\\script.bat")]
    [InlineData("\"C:\\Windows\\System32\\cmd.exe\" /c \"C:\\tools\\script.bat\"", "C:\\tools\\script.bat")]
    // cmd /c path â€” unquoted path
    [InlineData("cmd /c C:\\tools\\app.exe", "C:\\tools\\app.exe")]
    [InlineData("cmd /c script.bat", "script.bat")]
    // cmd /c start /B path â€” unquoted start with flag
    [InlineData("cmd /c start /B C:\\tools\\app.exe", "C:\\tools\\app.exe")]
    [InlineData("cmd /c start C:\\tools\\app.exe", "C:\\tools\\app.exe")]
    // cmd without /c â€” bare path
    [InlineData("cmd script.bat", "script.bat")]
    // cmd /c "path with spaces" â€” quoted path with spaces, with or without args
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
        // cmd /c start with no path â€” falls back to cmd executable
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
        // powershell with only flags and no script â€” falls back to powershell executable
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
                CreateTaskInfo(@"\App", @"C:\Program Files\app.exe")
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
                CreateTaskInfo(@"\App", @"C:\Program Files\app.exe", isPerUser: true, userSid: UserSid1)
            ]);
            s.AddFileExists(@"C:\Program Files\app.exe");
            s.AddFileSecurity(@"C:\Program Files\app.exe", fileSec);
        });

        var results = scanner.RunChecks();

        // UserSid1 should be excluded as task owner â€” no autorun finding
        var autorunFindings = results.Where(f =>
            f is { Category: StartupSecurityCategory.TaskScheduler, VulnerableSid: UserSid1 }).ToList();
        Assert.Empty(autorunFindings);
    }

    [Fact]
    public void RunChecks_TaskSchedulerWrapperPayload_Collected()
    {
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));
        var parentDirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.ClearStartupPaths();
            s.SetTaskSchedulerData([
                CreateTaskInfo(@"\Wrapper", "cmd.exe", @"/c ""C:\Tools\payload.bat""")
            ]);
            s.AddFileExists(@"C:\Tools\payload.bat");
            s.AddFileSecurity(@"C:\Tools\payload.bat", fileSec);
            s.AddDirectorySecurity(@"C:\Tools", parentDirSec);
        });

        var results = scanner.RunChecks();

        Assert.Contains(results, f =>
            f is { Category: StartupSecurityCategory.TaskScheduler, TargetDescription: @"C:\Tools\payload.bat", VulnerableSid: UserSid1 });
    }

    [Fact]
    public void RunChecks_TaskSchedulerWrapperPayload_UsesWorkingDirectory()
    {
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));
        var parentDirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var scanner = CreateScanner(s =>
        {
            s.ClearStartupPaths();
            s.SetTaskSchedulerData([
                CreateTaskInfo(@"\Wrapper", "cmd.exe", @"/c payload.bat", @"C:\Tools")
            ]);
            s.AddFileExists(@"C:\Tools\payload.bat");
            s.AddFileSecurity(@"C:\Tools\payload.bat", fileSec);
            s.AddDirectorySecurity(@"C:\Tools", parentDirSec);
        });

        var results = scanner.RunChecks();

        Assert.Contains(results, f =>
            f is { Category: StartupSecurityCategory.TaskScheduler, TargetDescription: @"C:\Tools\payload.bat", VulnerableSid: UserSid1 });
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

    [Fact]
    public void RunChecks_IfeoSubkeyWritable_Flagged()
    {
        var regSec = CreateRegSecurity(
            (AdminsSid, RegistryRights.FullControl, AccessControlType.Allow),
            (UserSid1, RegistryRights.SetValue, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddIfeoSubkeyName("notepad.exe");
            s.SetIfeoSubkeySecurity("notepad.exe", regSec);
        });

        var results = scanner.RunChecks();

        Assert.Contains(results, f =>
            f is { Category: StartupSecurityCategory.RegistryRunKey, VulnerableSid: UserSid1 } &&
            f.TargetDescription.Contains(@"Image File Execution Options\notepad.exe", StringComparison.OrdinalIgnoreCase));
    }

    // ===== IFEO per-subkey tests =====

    [Fact]
    public void RunChecks_IfeoSubkeyDebugger_CollectedAsAutorun()
    {
        // A debugger registered under an IFEO subkey (e.g. notepad.exe â†’ debugger.exe)
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

        // The debugger exe is writable â†’ autorun finding
        var ifeoAutorunFindings = results.Where(f =>
            f is { Category: StartupSecurityCategory.RegistryRunKey, TargetDescription: @"C:\Tools\debugger.exe" }).ToList();
        Assert.Single(ifeoAutorunFindings);
        Assert.Equal(UserSid1, ifeoAutorunFindings[0].VulnerableSid);
    }

    [Fact]
    public void RunChecks_IfeoSubkeyDebuggerAdminOnly_NotFlagged()
    {
        // Admin-only debugger binary â†’ no finding
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
            // No debugger path configured for this subkey
        });

        var results = scanner.RunChecks();

        // No findings for a subkey with no debugger path
        Assert.DoesNotContain(results, f => f.Category == StartupSecurityCategory.RegistryRunKey);
    }

}
