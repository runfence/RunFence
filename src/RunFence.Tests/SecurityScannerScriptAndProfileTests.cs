using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core.Models;
using RunFence.SecurityScanner;
using Xunit;

namespace RunFence.Tests;
public sealed class SecurityScannerScriptAndProfileTests : SecurityScannerTestBase
{
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

    [Fact]
    public void RunChecks_StartupShortcutWrapperPayload_CheckedAsAutorun()
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
            s.AddFolderFiles(@"C:\ProgramData\Startup", @"C:\ProgramData\Startup\wrapper.lnk");
            s.AddFileSecurity(@"C:\ProgramData\Startup\wrapper.lnk",
                CreateFileSecurity((AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow)));
            s.AddShortcutTarget(@"C:\ProgramData\Startup\wrapper.lnk", "cmd.exe", @"/c ""C:\Tools\payload.bat""", @"C:\Tools");
            s.AddFileExists(@"C:\Tools\payload.bat");
            s.AddFileSecurity(@"C:\Tools\payload.bat", fileSec);
            s.AddDirectorySecurity(@"C:\Tools", parentDirSec);
        });

        var results = scanner.RunChecks();

        Assert.Contains(results, f =>
            f is { Category: StartupSecurityCategory.StartupFolder, TargetDescription: @"C:\Tools\payload.bat", VulnerableSid: UserSid1 });
    }

    [Fact]
    public void RunChecks_StartupShortcutTarget_UsesWorkingDirectory()
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
            s.AddFolderFiles(@"C:\ProgramData\Startup", @"C:\ProgramData\Startup\relative.lnk");
            s.AddFileSecurity(@"C:\ProgramData\Startup\relative.lnk",
                CreateFileSecurity((AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow)));
            s.AddShortcutTarget(@"C:\ProgramData\Startup\relative.lnk", "app.exe", workingDirectory: @"C:\Tools");
            s.AddFileExists(@"C:\Tools\app.exe");
            s.AddFileSecurity(@"C:\Tools\app.exe", fileSec);
            s.AddDirectorySecurity(@"C:\Tools", parentDirSec);
        });

        var results = scanner.RunChecks();

        Assert.Contains(results, f =>
            f is { Category: StartupSecurityCategory.StartupFolder, TargetDescription: @"C:\Tools\app.exe", VulnerableSid: UserSid1 });
    }

    [Fact]
    public void RunChecks_StartupShortcutWrapperParseFailure_EmitsWarning()
    {
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var scanner = CreatePublicStartupScanner(dirSec, s =>
        {
            s.AddFolderFiles(@"C:\ProgramData\Startup", @"C:\ProgramData\Startup\broken.lnk");
            s.AddFileSecurity(@"C:\ProgramData\Startup\broken.lnk",
                CreateFileSecurity((AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow)));
            s.AddShortcutTarget(@"C:\ProgramData\Startup\broken.lnk", "cmd.exe", "/c start");
        });

        var results = scanner.RunChecks();

        Assert.Contains(results, f =>
            f is
            {
                Category: StartupSecurityCategory.StartupFolder,
                TargetDescription: @"C:\ProgramData\Startup\broken.lnk",
                VulnerableSid: "",
                VulnerablePrincipal: "Startup shortcut"
            } &&
            f.AccessDescription.Contains("could not be parsed", StringComparison.OrdinalIgnoreCase));
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

    // These tests use AutorunRegistryDataAccess (real path-building logic) intentionally: the scanner's
    // path-building logic depends on registry hive naming conventions that are best verified
    // against the real implementation rather than a fabricated test double.

    [Fact]
    public void GetWow6432RunKeyPaths_WithUserSid_ReturnsHkuPaths()
    {
        var dataAccess = new AutorunRegistryDataAccess();
        var paths = dataAccess.GetWow6432RunKeyPaths("S-1-5-21-123");

        Assert.Equal(4, paths.Count);
        Assert.Contains(paths, p => p.DisplayPath.Contains("HKU") && p.DisplayPath.Contains("Wow6432Node\\Run"));
        Assert.Contains(paths, p => p.DisplayPath.Contains("HKU") && p.DisplayPath.Contains("Wow6432Node\\RunOnce"));
    }

    [Fact]
    public void GetWow6432RunKeyPaths_WithNullUserSid_ReturnsHklmOnly()
    {
        var dataAccess = new AutorunRegistryDataAccess();
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

        // UserSid2 should be excluded as owner â€” no autorun findings for their own startup exe
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
                CreateTaskInfo(@"\Updater", @"C:\Users\TestUser\AppData\Local\app\updater.exe")
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

        // desktop.ini should NOT appear in ANY category â€” not as autorun exe nor as StartupFolder file
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

}
