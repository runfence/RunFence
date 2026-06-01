using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.SecurityScanner;
using RunFence.Tests.Helpers;
using Moq;
using Xunit;

namespace RunFence.Tests;

public sealed class SecurityScannerCompositionTests : SecurityScannerTestBase
{
    [Fact]
    public void SecurityScannerEnvironmentBuilder_CreateEnvironmentDataAccess_UsesInjectedLocalGroupPolicyReader()
    {
        var localGroupSid = "S-1-5-21-111-222-333-444";
        var foreignDomainSid = "S-1-5-21-999-888-777-666";
        var localGroupReader = new FocusedLocalGroupPolicyNativeReader("S-1-5-21-111-222-333")
        {
            ResolvedGroupNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [localGroupSid] = "TestGroup",
            },
            GroupMembers = new Dictionary<string, HashSet<string>?>(StringComparer.OrdinalIgnoreCase)
            {
                ["TestGroup"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { UserSid1 },
            },
        };

        var builders = CreateBuilders(localGroupPolicyReader: localGroupReader);
        var environment = builders.EnvironmentBuilder.CreateEnvironmentDataAccess();

        var result = environment.BulkLookupGroupMemberSids([localGroupSid, foreignDomainSid]);

        Assert.NotNull(result[localGroupSid]);
        Assert.Contains(UserSid1, result[localGroupSid]!);
        Assert.Null(result[foreignDomainSid]);
        Assert.Equal([localGroupSid], localGroupReader.LastResolvedSidBatch);
        Assert.Equal(["TestGroup"], localGroupReader.ResolvedMemberLookups);
    }

    [Fact]
    public void SecurityScannerCompositionFactory_CanWireFocusedDependenciesAndProduceFindings()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(UserSid1),
                FileSystemRights.WriteData,
                AccessControlType.Allow));
            var currentUserSid = WindowsIdentity.GetCurrent().User?.Value;
            if (!string.IsNullOrEmpty(currentUserSid))
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(currentUserSid),
                    FileSystemRights.FullControl,
                    AccessControlType.Allow));
            }

            new DirectoryInfo(tempRoot).SetAccessControl(security);
            var builders = CreateBuilders(driveRoots: new FocusedDriveRootNativeReader(tempRoot));

            var findings = builders.Factory.CreateDefaultScanner().RunChecks();

            Assert.Contains(findings, f => f.Category == StartupSecurityCategory.DiskRootAcl && f.TargetDescription == tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void SecurityScannerPolicyBuilder_CreateDiskRootScanner_UsesInjectedDriveRootDependency()
    {
        var findings = new List<StartupSecurityFinding>();
        var ctx = CreateScanContext(findings);
        var fileSystem = new FocusedFileSystemDataAccess(CreateDirSecurity((UserSid1, FileSystemRights.WriteData, AccessControlType.Allow)));
        var builders = CreateBuilders(driveRoots: new FocusedDriveRootNativeReader(@"D:\"));
        var aclCheck = builders.EnvironmentBuilder.CreateAclCheckHelper(new FocusedEnvironmentDataAccess());
        var scanner = builders.PolicyBuilder.CreateDiskRootScanner(fileSystem, aclCheck);

        scanner.ScanDiskRoots(ctx);

        Assert.Contains(findings, f => f.Category == StartupSecurityCategory.DiskRootAcl && f.TargetDescription == @"D:\");
    }

    [Fact]
    public void SecurityScannerAutorunBuilder_CreateMachineLevelRegistryScanner_UsesInjectedRegistryDependencies()
    {
        var findings = new List<StartupSecurityFinding>();
        var ctx = CreateScanContext(findings);
        var security = CreateRegSecurity((UserSid1, RegistryRights.SetValue, AccessControlType.Allow));
        var fileSystem = new FocusedFileSystemDataAccess(CreateDirSecurity((AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow)));
        var builders = CreateBuilders(autorunRegistry: new FocusedAutorunRegistryAccess(security));
        var scanner = builders.AutorunBuilder.CreateMachineLevelRegistryScanner(
            fileSystem,
            builders.EnvironmentBuilder.CreateAclCheckHelper(new FocusedEnvironmentDataAccess()));

        scanner.ScanMachineRegistryRunKeys(ctx);

        Assert.Contains(findings, f => f.Category == StartupSecurityCategory.RegistryRunKey && f.VulnerableSid == UserSid1);
    }

    [Fact]
    public void SecurityScannerPolicyBuilder_CreateMachineLevelPolicyScanner_UsesInjectedPolicyDependencies()
    {
        var findings = new List<StartupSecurityFinding>();
        var ctx = CreateScanContext(findings);
        var environment = new FocusedEnvironmentDataAccess(@"C:\ProgramData\Startup");
        var fileSystem = new FocusedFileSystemDataAccess(CreateDirSecurity((AdminsSid, FileSystemRights.FullControl, AccessControlType.Allow)));
        var builders = CreateBuilders(firewallPolicy: new FocusedFirewallPolicyDataAccess((true, false)));
        var aclCheck = builders.EnvironmentBuilder.CreateAclCheckHelper(environment);
        var autorunChecker = builders.AutorunBuilder.CreateAutorunChecker(fileSystem, aclCheck);
        var perUserScanner = builders.AutorunBuilder.CreatePerUserScanner(environment, fileSystem, aclCheck, autorunChecker);
        var scanner = builders.PolicyBuilder.CreateMachineLevelPolicyScanner(environment, fileSystem, aclCheck, perUserScanner);

        scanner.ScanWindowsFirewall(ctx);

        Assert.Contains(findings, f => f.Category == StartupSecurityCategory.FirewallPolicy);
    }


    private static TestCompositionBuilders CreateBuilders(
        ILoggingService? log = null,
        Action<string>? errorLogger = null,
        ILocalGroupPolicyNativeReader? localGroupPolicyReader = null,
        ShortcutResolver? shortcutResolver = null,
        IDriveRootNativeReader? driveRoots = null,
        IAutorunRegistryAccess? autorunRegistry = null,
        IWinlogonRegistryAccess? winlogonRegistry = null,
        IServiceRegistryAccess? serviceRegistry = null,
        IIfeoRegistryAccess? ifeoRegistry = null,
        IWindowsComponentRegistryAccess? windowsComponentRegistry = null,
        ITaskSchedulerDataAccess? taskScheduler = null,
        IGroupPolicyDataAccess? groupPolicy = null,
        IAccountPolicyDataAccess? accountPolicy = null,
        IFirewallPolicyDataAccess? firewallPolicy = null)
    {
        var resolvedLog = log ?? Mock.Of<ILoggingService>();
        var resolvedAutorunRegistry = autorunRegistry ?? new FocusedAutorunRegistryAccess();
        var resolvedGroupPolicy = groupPolicy ?? new FocusedGroupPolicyDataAccess();
        var resolvedServiceRegistry = serviceRegistry ?? new FocusedServiceRegistryAccess();
        var environmentBuilder = new SecurityScannerEnvironmentBuilder(
            resolvedLog,
            errorLogger ?? (_ => { }),
            localGroupPolicyReader ?? new FocusedLocalGroupPolicyNativeReader(),
            shortcutResolver ?? new ShortcutResolver(),
            resolvedAutorunRegistry);
        var autorunBuilder = new SecurityScannerAutorunBuilder(
            resolvedLog,
            resolvedAutorunRegistry,
            winlogonRegistry ?? new FocusedWinlogonRegistryAccess(),
            ifeoRegistry ?? new FocusedIfeoRegistryAccess(),
            windowsComponentRegistry ?? new FocusedWindowsComponentRegistryAccess(),
            resolvedGroupPolicy);
        var policyBuilder = new SecurityScannerPolicyBuilder(
            resolvedLog,
            driveRoots ?? new FocusedDriveRootNativeReader(@"D:\"),
            taskScheduler ?? new FocusedTaskSchedulerDataAccess(),
            resolvedGroupPolicy,
            resolvedServiceRegistry,
            firewallPolicy ?? new FocusedFirewallPolicyDataAccess(null),
            accountPolicy ?? new FocusedAccountPolicyDataAccess());
        var factory = new SecurityScannerCompositionFactory(environmentBuilder, autorunBuilder, policyBuilder);

        return new TestCompositionBuilders(factory, environmentBuilder, autorunBuilder, policyBuilder);
    }

    private static ScanContext CreateScanContext(List<StartupSecurityFinding> findings) =>
        new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { AdminsSid },
            findings,
            new HashSet<(string, string)>(CaseInsensitiveTupleComparer.Instance),
            new AutorunContext(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, StartupSecurityCategory>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, List<AutorunCommandContext>>(StringComparer.OrdinalIgnoreCase),
                []),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            null,
            null);

    private sealed class FocusedEnvironmentDataAccess(string? publicStartupPath = null) : IEnvironmentDataAccess
    {
        public string? GetPublicStartupPath() => publicStartupPath;
        public string? GetCurrentUserStartupPath() => null;
        public string? GetCurrentUserSid() => null;
        public string? GetInteractiveUserSid() => null;
        public HashSet<string> GetAdminMemberSids() => new(StringComparer.OrdinalIgnoreCase) { AdminsSid };
        public List<(string Sid, string? ProfilePath)> GetAllLocalUserProfiles() => [];
        public string? GetInteractiveUserProfilePath(string sid) => null;
        public HashSet<string>? TryGetGroupMemberSids(string groupSid) => null;
        public Dictionary<string, HashSet<string>?> BulkLookupGroupMemberSids(IReadOnlyList<string> sids) => new(StringComparer.OrdinalIgnoreCase);
        public string ResolveDisplayName(string sidString) => sidString;
    }

    private sealed class FocusedLocalGroupPolicyNativeReader(string? localDomainSid = null) : ILocalGroupPolicyNativeReader
    {
        public Dictionary<string, string> ResolvedGroupNames { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, HashSet<string>?> GroupMembers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> LastResolvedSidBatch { get; } = [];
        public List<string> ResolvedMemberLookups { get; } = [];

        public string? GetLocalDomainSid() => localDomainSid;

        public Dictionary<string, string> ResolveLocalGroupNames(IReadOnlyList<string> sidStrings)
        {
            LastResolvedSidBatch.Clear();
            LastResolvedSidBatch.AddRange(sidStrings);

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sid in sidStrings)
            {
                if (ResolvedGroupNames.TryGetValue(sid, out var groupName))
                    result[sid] = groupName;
            }

            return result;
        }

        public HashSet<string>? GetLocalGroupMemberSids(string groupName)
        {
            ResolvedMemberLookups.Add(groupName);
            return GroupMembers.GetValueOrDefault(groupName);
        }
    }

    private sealed class FocusedFileSystemDataAccess(DirectorySecurity directorySecurity) : IFileSystemDataAccess
    {
        public bool DirectoryExists(string path) => true;
        public bool FileExists(string path) => false;
        public DirectorySecurity GetDirectorySecurity(string path) => directorySecurity;
        public FileSecurity GetFileSecurity(string path) => throw new FileNotFoundException();
        public string[] GetFilesInFolder(string folderPath) => [];
        public ShortcutTargetInfo? ResolveShortcutTarget(string lnkPath) => null;
    }

    private sealed class ThrowingFileSystemDataAccess(Exception exception) : IFileSystemDataAccess
    {
        public bool DirectoryExists(string path) => true;
        public bool FileExists(string path) => false;
        public DirectorySecurity GetDirectorySecurity(string path) => throw exception;
        public FileSecurity GetFileSecurity(string path) => throw exception;
        public string[] GetFilesInFolder(string folderPath) => [];
        public ShortcutTargetInfo? ResolveShortcutTarget(string lnkPath) => null;
    }

    private sealed class FocusedDriveRootNativeReader(string root) : IDriveRootNativeReader
    {
        public IEnumerable<string> GetDriveRoots()
        {
            yield return root;
        }
    }

    private sealed class FocusedAutorunRegistryAccess(RegistrySecurity? security = null) : IAutorunRegistryAccess
    {
        public RegistrySecurity? GetRegistryKeySecurity(RegistryKey hive, string subKeyPath) => security;
        public List<string> GetRegistryAutorunPaths(RegistryKey hive, string subKeyPath) => [];
        public List<(string SubKeyPath, string DisplayPath)> GetWow6432RunKeyPaths(string? userSid) => [];
    }

    private sealed class ThrowingAutorunRegistryAccess(Exception exception) : IAutorunRegistryAccess
    {
        public RegistrySecurity? GetRegistryKeySecurity(RegistryKey hive, string subKeyPath) => throw exception;
        public List<string> GetRegistryAutorunPaths(RegistryKey hive, string subKeyPath) => throw exception;
        public List<(string SubKeyPath, string DisplayPath)> GetWow6432RunKeyPaths(string? userSid) => [];
    }

    private sealed class FocusedWinlogonRegistryAccess : IWinlogonRegistryAccess
    {
        public RegistrySecurity? GetWinlogonRegistryKeySecurity() => null;
        public List<string> GetWinlogonExePaths() => [];
        public List<AppInitDllEntry> GetAppInitDllEntries() => [];
    }

    private sealed class FocusedIfeoRegistryAccess : IIfeoRegistryAccess
    {
        public RegistrySecurity? GetIfeoRegistryKeySecurity() => null;
        public RegistrySecurity? GetIfeoWow6432RegistryKeySecurity() => null;
        public List<IfeoSubkeyInfo> GetIfeoSubkeys() => [];
    }

    private sealed class FocusedWindowsComponentRegistryAccess : IWindowsComponentRegistryAccess
    {
        public List<RegistryDllEntry> GetPrintMonitorEntries() => [];
        public List<(RegistrySecurity? Security, List<string> DllPaths)> GetLsaPackageEntries() => [];
        public List<RegistryDllEntry> GetNetworkProviderEntries() => [];
    }

    private sealed class FocusedTaskSchedulerDataAccess : ITaskSchedulerDataAccess
    {
        public List<ScheduledTaskInfo> GetTaskSchedulerData() => [];
    }

    private sealed class FocusedGroupPolicyDataAccess : IGroupPolicyDataAccess
    {
        public string GetGpScriptsDir(string userSid) => "";
        public string GetMachineGpScriptsDir() => "";
        public string GetMachineGpUserScriptsDir() => "";
        public List<string> GetMachineGpScriptPaths() => [];
        public string GetSharedWrapperScriptsDir() => "";
        public List<string> GetLogonScriptPaths(string userSid) => [];
    }

    private sealed class FocusedServiceRegistryAccess : IServiceRegistryAccess
    {
        public List<ServiceInfo> GetAutoStartServices() => [];
    }

    private sealed class FocusedFirewallPolicyDataAccess((bool IsDisabled, bool IsStopped)? serviceState) : IFirewallPolicyDataAccess
    {
        public List<(string ProfileName, bool Enabled)>? GetFirewallProfileStates() => null;
        public (bool IsDisabled, bool IsStopped)? GetWindowsFirewallServiceState() => serviceState;
    }

    private sealed class FocusedAccountPolicyDataAccess : IAccountPolicyDataAccess
    {
        public int? GetAccountLockoutThreshold() => null;
        public bool? GetAdminAccountLockoutEnabled() => null;
        public bool? GetBlankPasswordRestrictionEnabled() => null;
    }

    private sealed record TestCompositionBuilders(
        SecurityScannerCompositionFactory Factory,
        SecurityScannerEnvironmentBuilder EnvironmentBuilder,
        SecurityScannerAutorunBuilder AutorunBuilder,
        SecurityScannerPolicyBuilder PolicyBuilder);
}
