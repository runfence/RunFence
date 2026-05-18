using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core.Models;
using RunFence.SecurityScanner;
using Xunit;

namespace RunFence.Tests;
public sealed class SecurityScannerLsaAndAccountPolicyTests : SecurityScannerTestBase
{
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
    public void RunChecks_LsaBareModuleName_ResolvedAcrossSystemDirsWithoutAggregateWarning()
    {
        var system32Dll = GetSystem32Path("authpkg.dll");
        var sysWow64Dll = GetSysWow64Path("authpkg.dll");
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddLsaPackageEntry(null, ["authpkg"]);
            s.AddFileExists(system32Dll);
            s.AddFileExists(sysWow64Dll);
            s.AddFileSecurity(system32Dll, fileSec);
            s.AddFileSecurity(sysWow64Dll, fileSec);
            s.AddDirectorySecurity(Path.GetDirectoryName(system32Dll)!, dirSec);
            s.AddDirectorySecurity(Path.GetDirectoryName(sysWow64Dll)!, dirSec);
        });

        var results = scanner.RunChecks();

        Assert.Contains(results, f =>
            f is { Category: StartupSecurityCategory.RegistryRunKey, TargetDescription: var target, VulnerableSid: UserSid1 } &&
            (string.Equals(target, system32Dll, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(target, sysWow64Dll, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(results, f =>
            f is
            {
                Category: StartupSecurityCategory.RegistryRunKey,
                TargetDescription: var target,
                VulnerableSid: "",
                VulnerablePrincipal: "Windows component DLL"
            } &&
            target.Contains("authpkg", StringComparison.OrdinalIgnoreCase) &&
            f.AccessDescription.Contains("multiple architecture-specific paths", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RunChecks_LsaBareModuleName_MissingResolvedItemWithSafeParent_NotReported()
    {
        var system32Dll = GetSystem32Path("authpkg.dll");
        var sysWow64Dll = GetSysWow64Path("authpkg.dll");
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddLsaPackageEntry(null, ["authpkg"]);
            s.AddFileExists(system32Dll);
            s.AddFileSecurity(system32Dll, fileSec);
            s.AddDirectorySecurity(Path.GetDirectoryName(system32Dll)!, dirSec);
            s.AddDirectorySecurity(Path.GetDirectoryName(sysWow64Dll)!, dirSec);
        });

        var results = scanner.RunChecks();

        Assert.Contains(results, f =>
            f is { Category: StartupSecurityCategory.RegistryRunKey, TargetDescription: var target, VulnerableSid: UserSid1 } &&
            string.Equals(target, system32Dll, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(results, f =>
            f is { Category: StartupSecurityCategory.RegistryRunKey, TargetDescription: var target } &&
            string.Equals(target, sysWow64Dll, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RunChecks_LsaBareModuleName_MissingResolvedItemWithWritableParent_Flagged()
    {
        var sysWow64Dll = GetSysWow64Path("authpkg.dll");
        var sysWow64Dir = Path.GetDirectoryName(sysWow64Dll)!;
        var dirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddLsaPackageEntry(null, ["authpkg"]);
            s.AddDirectorySecurity(sysWow64Dir, dirSec);
        });

        var results = scanner.RunChecks();

        Assert.Contains(results, f =>
            f is { Category: StartupSecurityCategory.RegistryRunKey, TargetDescription: var target, VulnerableSid: UserSid1 } &&
            string.Equals(target, sysWow64Dll, StringComparison.OrdinalIgnoreCase) &&
            f.AccessDescription.Contains(sysWow64Dir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RunChecks_LsaBareModuleName_MissingResolvedItemWithCreatableAncestor_Flagged()
    {
        var sysWow64Dll = GetSysWow64Path("authpkg.dll");
        var sysWow64Dir = Path.GetDirectoryName(sysWow64Dll)!;
        var windowsDirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.AppendData, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddLsaPackageEntry(null, ["authpkg"]);
            s.AddDirectorySecurity(WindowsDir, windowsDirSec);
        });

        var results = scanner.RunChecks();

        Assert.Contains(results, f =>
            f is { Category: StartupSecurityCategory.RegistryRunKey, TargetDescription: var target, VulnerableSid: UserSid1 } &&
            string.Equals(target, sysWow64Dll, StringComparison.OrdinalIgnoreCase) &&
            f.AccessDescription.Contains(WindowsDir, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(results, f =>
            f is { Category: StartupSecurityCategory.RegistryRunKey, TargetDescription: var target, VulnerableSid: UserSid1 } &&
            string.Equals(target, sysWow64Dir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RunChecks_LsaBareModuleName_MissingResolvedItemWithOnlyCreateFilesOnAncestor_NotFlagged()
    {
        var sysWow64Dll = GetSysWow64Path("authpkg.dll");
        var windowsDirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddLsaPackageEntry(null, ["authpkg"]);
            s.AddDirectorySecurity(WindowsDir, windowsDirSec);
        });

        var results = scanner.RunChecks();

        Assert.DoesNotContain(results, f =>
            f is { Category: StartupSecurityCategory.RegistryRunKey, TargetDescription: var target, VulnerableSid: UserSid1 } &&
            string.Equals(target, sysWow64Dll, StringComparison.OrdinalIgnoreCase));
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
            // Set up the GP scripts directory for the user â€” this is what the scanner checks
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

}
