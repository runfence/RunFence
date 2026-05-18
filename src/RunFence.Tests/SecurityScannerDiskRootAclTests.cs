using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core.Models;
using RunFence.SecurityScanner;
using Xunit;

namespace RunFence.Tests;
public sealed class SecurityScannerDiskRootAclTests : SecurityScannerTestBase
{
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
        // GetDirectorySecurity throws (access denied) â€” the root must be skipped without crash or finding.
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

    [Fact]
    public void RunChecks_IfeoWow6432SubkeyDebugger_CollectedAsAutorun()
    {
        var fileSec = CreateFileSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow),
            (UserSid1, FileSystemRights.WriteData, AccessControlType.Allow));
        var parentDirSec = CreateDirSecurity(
            (AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow));

        var scanner = CreateIsolatedScanner(s =>
        {
            s.AddIfeoWow6432Subkey("wowapp.exe", debuggerPath: @"C:\Tools\wowdbg.exe");
            s.AddFileExists(@"C:\Tools\wowdbg.exe");
            s.AddFileSecurity(@"C:\Tools\wowdbg.exe", fileSec);
            s.AddDirectorySecurity(@"C:\Tools", parentDirSec);
        });

        var results = scanner.RunChecks();

        Assert.Contains(results, f =>
            f is { Category: StartupSecurityCategory.RegistryRunKey, TargetDescription: @"C:\Tools\wowdbg.exe", VulnerableSid: UserSid1 });
    }
}
