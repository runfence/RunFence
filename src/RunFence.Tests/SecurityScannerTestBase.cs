using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core.Models;
using RunFence.SecurityScanner;
using RunFence.Tests.TestData;
using RunFence.Tests.Helpers;
using Xunit;
using Scanner = RunFence.SecurityScanner.SecurityScanner;

namespace RunFence.Tests;

public abstract class SecurityScannerTestBase
{
    // Well-known SIDs for testing
    protected static readonly string AdminsSid = new SecurityIdentifier(
        WellKnownSidType.BuiltinAdministratorsSid, null).Value;

    protected static readonly string SystemSid = new SecurityIdentifier(
        WellKnownSidType.LocalSystemSid, null).Value;
    protected const string NetworkConfigurationOperatorsSid = "S-1-5-32-556";

    protected const string TrustedInstallerSid =
        "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    // Fake non-admin user SIDs
    protected const string UserSid1 = "S-1-5-21-1111111111-2222222222-3333333333-1001";
    protected const string UserSid2 = "S-1-5-21-1111111111-2222222222-3333333333-1002";
    protected const string CurrentUserSid = "S-1-5-21-999999999-888888888-777777777-500";
    protected const string InteractiveUserSid = "S-1-5-21-444444444-555555555-666666666-1001";
    protected static readonly string WindowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    // Admin member SID (individual admin user)
    protected const string AdminUserSid = "S-1-5-21-1111111111-2222222222-3333333333-500";

    protected Scanner CreateScanner(Action<TestScannerDataAccess>? configure = null)
        => new SecurityScannerTestDataBuilder()
            .WithCurrentUserSid(CurrentUserSid)
            .WithInteractiveUserSid(InteractiveUserSid)
            .WithAdminMemberSids(AdminsSid, CurrentUserSid, "S-1-5-32-544")
            .ConfigureRaw(configure)
            .Build();

    protected static Scanner CreateIsolatedScanner(Action<TestScannerDataAccess>? configure = null)
        => new SecurityScannerTestDataBuilder()
            .WithCurrentUserSid(null)
            .WithAdminMemberSids(AdminsSid, "S-1-5-32-544")
            .WithoutStartupPaths()
            .ConfigureRaw(configure)
            .Build();

    /// <summary>
    /// Creates a scanner with public startup folder configured, no current-user or interactive folders.
    /// </summary>
    protected Scanner CreatePublicStartupScanner(DirectorySecurity dirSec,
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

    protected static DirectorySecurity CreateDirSecurity(params (string Sid, FileSystemRights Rights, AccessControlType Type)[] aces)
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

    protected static FileSecurity CreateFileSecurity(params (string Sid, FileSystemRights Rights, AccessControlType Type)[] aces)
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

    protected static RegistrySecurity CreateRegSecurity(params (string Sid, RegistryRights Rights, AccessControlType Type)[] aces)
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

    protected static ScheduledTaskInfo CreateTaskInfo(
        string taskPath,
        string executablePath,
        string? arguments = null,
        string? workingDirectory = null,
        bool isPerUser = false,
        string? userSid = null,
        string? principalSid = null,
        string? taskSecurityDescriptor = null)
    {
        return new ScheduledTaskInfo(
            taskPath,
            [new ScheduledTaskActionInfo(0, executablePath, arguments, workingDirectory)],
            principalSid ?? userSid,
            taskSecurityDescriptor,
            isPerUser,
            userSid);
    }

    protected static string GetSystem32Path(string fileName) => Path.Combine(WindowsDir, "System32", fileName);
    protected static string GetSysWow64Path(string fileName) => Path.Combine(WindowsDir, "SysWOW64", fileName);

}
