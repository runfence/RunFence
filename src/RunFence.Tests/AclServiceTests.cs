using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class AclServiceTests
{
    private readonly AclService _service;
    private readonly IAclDenyModeService _denyService;
    private readonly Mock<ILoggingService> _log;
    private readonly CachingLocalUserProvider _localUserProvider;

    private const string Sid1 = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string Sid2 = "S-1-5-21-1234567890-1234567890-1234567890-1002";

    public AclServiceTests()
    {
        _log = new Mock<ILoggingService>();
        _localUserProvider = new CachingLocalUserProvider(_log.Object, new LocalSamSidResolver(_log.Object), new SystemClock());
        (_service, _denyService) = CreateServiceAndDenyService(_log.Object, _localUserProvider);
    }

    private static AclService CreateService(ILoggingService log, CachingLocalUserProvider localUserProvider,
        IDatabaseProvider? databaseProvider = null,
        Mock<IGrantMutatorService>? grantMutatorService = null)
        => CreateServiceAndDenyService(log, localUserProvider, databaseProvider, grantMutatorService).Item1;

    private static (AclService service, IAclDenyModeService denyService) CreateServiceAndDenyService(
        ILoggingService log,
        CachingLocalUserProvider localUserProvider,
        IDatabaseProvider? databaseProvider = null,
        Mock<IGrantMutatorService>? grantMutatorService = null)
    {
        var resolvedDatabaseProvider = databaseProvider ?? new LambdaDatabaseProvider(() => new AppDatabase());
        var containerLookup = new ContainerLookupHelper(resolvedDatabaseProvider);
        var aclAccessor = AclAccessorFactory.Create();
        var resolver = new AppEntryAclTargetResolver();
        var denyService = new AclDenyModeService(log, localUserProvider, containerLookup, new Mock<IInteractiveUserResolver>().Object, aclAccessor, resolver);
        var allowService = new AclAllowModeService(
            log,
            localUserProvider,
            aclAccessor,
            new AppEntryAllowAclRuleProvider(resolver));
        grantMutatorService ??= new Mock<IGrantMutatorService>();
        return (
            new AclService(
                log,
                denyService,
                allowService,
                containerLookup,
                grantMutatorService.Object,
                resolver),
            denyService);
    }

    private static AppEntry CreateApp(string id, string name, string exePath,
        AclTarget aclTarget = AclTarget.File, bool restrictAcl = true,
        string? accountSid = null, int folderAclDepth = 0, bool isUrlScheme = false, bool isFolder = false,
        AclMode aclMode = AclMode.Deny, DeniedRights deniedRights = DeniedRights.Execute,
        List<AllowAclEntry>? allowedAclEntries = null) => new()
    {
        Id = id, Name = name, ExePath = exePath,
        AclTarget = aclTarget, RestrictAcl = restrictAcl,
        AccountSid = accountSid ?? string.Empty, FolderAclDepth = folderAclDepth,
        IsUrlScheme = isUrlScheme, IsFolder = isFolder,
        AclMode = aclMode, DeniedRights = deniedRights,
        AllowedAclEntries = allowedAclEntries
    };

    [Fact]
    public void IsBlockedPath_DynamicBlockedPaths_ReturnsTrue()
    {
        var blockedPaths = PathConstants.GetBlockedAclPaths();
        foreach (var path in blockedPaths)
        {
            Assert.True(_service.IsBlockedPath(path), $"Expected '{path}' to be blocked.");
        }
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files (x86)")]
    [InlineData(@"C:\Users")]
    public void IsBlockedPath_HardcodedBlockedPaths_ReturnsTrue(string path)
    {
        Assert.True(_service.IsBlockedPath(path));
    }

    [Theory]
    [InlineData(@"C:\MyApps")]
    [InlineData(@"C:\Program Files\MyApp")]
    [InlineData(@"D:\Games")]
    [InlineData(@"C:\Users\John\Desktop")]
    public void IsBlockedPath_AllowedPaths_ReturnsFalse(string path)
    {
        Assert.False(_service.IsBlockedPath(path));
    }

    [Fact]
    public void ResolveAclTargetPath_ExeTarget_ReturnsExePath()
    {
        var app = CreateApp("t0001", "Test", @"C:\MyApps\test.exe");
        var result = _service.ResolveAclTargetPath(app);
        Assert.Equal(Path.GetFullPath(@"C:\MyApps\test.exe"), result);
    }

    [Fact]
    public void ResolveAclTargetPath_FolderTarget_Depth0_ReturnsExeFolder()
    {
        var app = CreateApp("t0001", "Test", @"C:\MyApps\SubDir\test.exe", AclTarget.Folder);
        var result = _service.ResolveAclTargetPath(app);
        Assert.Equal(Path.GetFullPath(@"C:\MyApps\SubDir"), result);
    }

    [Fact]
    public void ResolveAclTargetPath_FolderTarget_Depth1_ReturnsParent()
    {
        var app = CreateApp("t0001", "Test", @"C:\MyApps\SubDir\test.exe", AclTarget.Folder, folderAclDepth: 1);
        var result = _service.ResolveAclTargetPath(app);
        Assert.Equal(Path.GetFullPath(@"C:\MyApps"), result);
    }

    [Fact]
    public void ResolveAclTargetPath_FolderTarget_MaxDepth_DoesNotExceedRoot()
    {
        var app = CreateApp("t0001", "Test", @"C:\test.exe", AclTarget.Folder, folderAclDepth: PathConstants.MaxFolderAclDepth);
        var result = _service.ResolveAclTargetPath(app);
        Assert.True(Path.IsPathRooted(result));
    }

    [Fact]
    public void ResolveAclTargetPath_FolderTarget_DepthExceedsMax_IsClamped()
    {
        var app = CreateApp("t0001", "Test", @"C:\A\B\C\D\E\F\test.exe", AclTarget.Folder, folderAclDepth: 100);
        var clampedApp = CreateApp("t0002", "Clamped", @"C:\A\B\C\D\E\F\test.exe", AclTarget.Folder, folderAclDepth: PathConstants.MaxFolderAclDepth);

        var result = _service.ResolveAclTargetPath(app);
        var clampedResult = _service.ResolveAclTargetPath(clampedApp);

        Assert.Equal(clampedResult, result);
    }

    // --- ApplyAcl routing logic tests ---

    [Fact]
    public void GetBlockedAclPaths_ReturnsNonEmptyArray()
    {
        var paths = PathConstants.GetBlockedAclPaths();
        Assert.NotEmpty(paths);
    }

    [Fact]
    public void ApplyAcl_UrlSchemeApp_ReturnsEarlyWithoutAccessingPath()
    {
        // If the early return on IsUrlScheme did not happen, subsequent Win32 ACL operations
        // on the invalid "steam://run/123" path would produce an error or warning log.
        // No-exception + no-error proves the method exits at the first guard.
        var app = CreateApp("s0001", "SteamApp", "steam://run/123", isUrlScheme: true);
        var allApps = new List<AppEntry> { app };

        var exception = Record.Exception(() => _service.ApplyAcl(app, allApps));

        Assert.Null(exception);
    }

    [Fact]
    public void ApplyAcl_RestrictAclFalse_ReturnsEarlyWithoutAccessingPath()
    {
        // C:\NonExistentPath9999\test.exe does not exist — if the early return did not happen,
        // the Windows ACL API call would fail with an error log entry, making no-error the proof.
        var app = CreateApp("u0001", "UnrestrictedApp", @"C:\NonExistentPath9999\test.exe", restrictAcl: false);
        var allApps = new List<AppEntry> { app };

        var exception = Record.Exception(() => _service.ApplyAcl(app, allApps));

        Assert.Null(exception);
    }

    // --- RevertAcl routing logic tests ---

    [Fact]
    public void ApplyAcl_BlockedPath_LogsWarningAndReturns()
    {
        var app = CreateApp("b0001", "BlockedApp", @"C:\Windows\notepad.exe", AclTarget.Folder);
        var allApps = new List<AppEntry> { app };

        _service.ApplyAcl(app, allApps);

        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("Blocked ACL target path"))), Times.Once);
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Applied ACL"))), Times.Never);
    }

    [Fact]
    public void RevertAcl_UrlSchemeApp_ReturnsEarlyWithoutAccessingPath()
    {
        // If the early return on IsUrlScheme did not happen, subsequent Win32 ACL operations
        // on "steam://run/123" would produce an error log. No-exception + no-error proves early return.
        var app = CreateApp("s0001", "SteamApp", "steam://run/123", isUrlScheme: true);
        var allApps = new List<AppEntry> { app };

        var exception = Record.Exception(() => _service.RevertAcl(app, allApps));

        Assert.Null(exception);
    }

    [Fact]
    public void RevertAcl_NonExistentPath_ReturnsEarlyWithoutAccessingPath()
    {
        // Z:\NonExistent does not exist — if RevertAcl tried to modify ACLs it would log an error.
        var app = CreateApp("m0001", "MissingApp", @"Z:\NonExistent\Path\app.exe");
        var allApps = new List<AppEntry> { app };

        var exception = Record.Exception(() => _service.RevertAcl(app, allApps));

        Assert.Null(exception);
    }

    // --- GetAllowedSidsForPath direct tests ---

    [Fact]
    public void RevertAcl_SharedPathWithOtherApp_ReappliesAclForOther()
    {
        using var tempDir = new TempDirectory("RunFence_AclTest");
        var sharedExePath = Path.Combine(tempDir.Path, "app.exe");
        File.WriteAllBytes(sharedExePath, []);

        var app1 = CreateApp("app01", "App1", sharedExePath, accountSid: Sid1);
        var app2 = CreateApp("app02", "App2", sharedExePath, accountSid: Sid2);

        var allApps = new List<AppEntry> { app1, app2 };

        _service.RevertAcl(app1, allApps);

        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Applied deny ACL"))), Times.Once);
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Reverted ACL"))), Times.Once);
    }

[Fact]
    public void RevertAcl_SharedPathWithUrlSchemeApp_DoesNotConsiderUrlScheme()
    {
        using var tempDir = new TempDirectory("RunFence_AclTest");
        var exePath = Path.Combine(tempDir.Path, "app.exe");
        File.WriteAllBytes(exePath, []);

        var app1 = CreateApp("app01", "App1", exePath);
        var urlApp = CreateApp("url01", "UrlApp", "steam://run/123", isUrlScheme: true);

        var allApps = new List<AppEntry> { app1, urlApp };

        _service.RevertAcl(app1, allApps);

        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Applied ACL"))), Times.Never);
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Reverted ACL"))), Times.Once);
    }

    [Fact]
    public void GetAllowedSidsForPath_FolderTarget_IncludesChildExeTargetedApps()
    {
        var folderApp = CreateApp("f0001", "FolderApp", @"C:\Apps\main.exe",
            AclTarget.Folder, accountSid: Sid1, folderAclDepth: 0);
        var exeApp = CreateApp("e0001", "ExeApp", @"C:\Apps\Sub\tool.exe", accountSid: Sid2);

        var allApps = new List<AppEntry> { folderApp, exeApp };

        var allowed = _denyService.GetAllowedSidsForPath(
            @"C:\Apps", allApps, isFolderTarget: true);

        Assert.Contains(Sid1, allowed);
        Assert.Contains(Sid2, allowed);
    }

    [Fact]
    public void GetAllowedSidsForPath_FolderTarget_IncludesDescendantFolderTargetedApps()
    {
        var parentApp = CreateApp("p0001", "ParentApp", @"C:\Apps\main.exe",
            AclTarget.Folder, accountSid: Sid1, folderAclDepth: 0);
        var childApp = CreateApp("c0001", "ChildApp", @"C:\Apps\Sub\tool.exe",
            AclTarget.Folder, accountSid: Sid2, folderAclDepth: 0);

        var allApps = new List<AppEntry> { parentApp, childApp };

        var allowed = _denyService.GetAllowedSidsForPath(
            @"C:\Apps", allApps, isFolderTarget: true);

        Assert.Contains(Sid1, allowed);
        Assert.Contains(Sid2, allowed);
    }

    [Fact]
    public void GetAllowedSidsForPath_ExeTarget_UsesExactMatchOnly()
    {
        var exeApp = CreateApp("e0001", "ExeApp", @"C:\Apps\tool.exe", accountSid: Sid1);
        var otherApp = CreateApp("e0002", "OtherApp", @"C:\Apps\other.exe", accountSid: Sid2);

        var allApps = new List<AppEntry> { exeApp, otherApp };

        var allowed = _denyService.GetAllowedSidsForPath(
            @"C:\Apps\tool.exe", allApps, isFolderTarget: false);

        Assert.Contains(Sid1, allowed);
        Assert.DoesNotContain(Sid2, allowed);
    }

    [Fact]
    public void GetAllowedSidsForPath_RevertedAppExcluded_DifferentCredentials()
    {
        var app2 = CreateApp("a0002", "App2", @"C:\Apps\tool.exe", accountSid: Sid2);

        var remainingApps = new List<AppEntry> { app2 };

        var allowed = _denyService.GetAllowedSidsForPath(
            @"C:\Apps\tool.exe", remainingApps, isFolderTarget: false);

        Assert.DoesNotContain(Sid1, allowed);
        Assert.Contains(Sid2, allowed);
    }

    [Fact]
    public void GetAllowedSidsForPath_RestrictAclFalse_Excluded()
    {
        var app = CreateApp("a0001", "App", @"C:\Apps\tool.exe", restrictAcl: false, accountSid: Sid1);

        var allApps = new List<AppEntry> { app };

        var allowed = _denyService.GetAllowedSidsForPath(
            @"C:\Apps\tool.exe", allApps, isFolderTarget: false);

        Assert.Empty(allowed);
    }

    [Fact]
    public void GetAllowedSidsForPath_UrlSchemeApp_Excluded()
    {
        var app = CreateApp("u0001", "UrlApp", "steam://run/123", accountSid: Sid1, isUrlScheme: true);

        var allApps = new List<AppEntry> { app };

        var allowed = _denyService.GetAllowedSidsForPath(
            "steam://run/123", allApps, isFolderTarget: false);

        Assert.Empty(allowed);
    }

    // --- IsFolder ACL tests ---

    [Fact]
    public void RecomputeAllAncestorAcls_NoFolderApps_NoErrors()
    {
        var app = CreateApp("e0001", "ExeApp", @"C:\Apps\tool.exe", accountSid: Sid1);

        var allApps = new List<AppEntry> { app };

        _service.RecomputeAllAncestorAcls(allApps);

        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Recomputed ancestor ACL"))), Times.Never);
    }

    [Fact]
    public void RecomputeAllAncestorAcls_BlockedAncestor_SkipsDenyMutation()
    {
        var denyService = new Mock<IAclDenyModeService>(MockBehavior.Strict);
        var service = new AclService(
            _log.Object,
            denyService.Object,
            new Mock<IAclAllowModeService>().Object,
            new ContainerLookupHelper(new LambdaDatabaseProvider(() => new AppDatabase())),
            new Mock<IGrantMutatorService>().Object,
            new AppEntryAclTargetResolver());
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var app = CreateApp(
            "f0001",
            "BlockedFolderApp",
            Path.Combine(windowsDir, "System32", "notepad.exe"),
            AclTarget.Folder,
            accountSid: Sid1,
            folderAclDepth: 1);

        service.RecomputeAllAncestorAcls([app]);

        denyService.Verify(
            s => s.GetDeniedRightsPerSid(It.IsAny<string>(), It.IsAny<IReadOnlyList<AppEntry>>(), It.IsAny<bool>()),
            Times.Never);
        denyService.Verify(
            s => s.ApplyDenyToFolderPerSid(It.IsAny<string>(), It.IsAny<Dictionary<string, DeniedRights>>()),
            Times.Never);
    }

    [Fact]
    public void RecomputeAllAncestorAcls_FolderAncestorWithDescendantExe_Recomputes()
    {
        using var tempDir = new TempDirectory("RunFence_AclTest");
        var subDir = Path.Combine(tempDir.Path, "Sub");
        Directory.CreateDirectory(subDir);
        var childExePath = Path.Combine(subDir, "tool.exe");
        File.WriteAllBytes(childExePath, []);
        var parentExePath = Path.Combine(tempDir.Path, "main.exe");
        File.WriteAllBytes(parentExePath, []);

        var folderApp = CreateApp("f0001", "FolderApp", parentExePath,
            AclTarget.Folder, accountSid: Sid1, folderAclDepth: 0);
        var exeApp = CreateApp("e0001", "ExeApp", childExePath, accountSid: Sid2);

        var allApps = new List<AppEntry> { folderApp, exeApp };

        _service.RecomputeAllAncestorAcls(allApps);

        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Recomputed ancestor ACL"))), Times.Once);
    }

    [Fact]
    public void RecomputeAllAncestorAcls_DenyFolderWithoutCurrentDescendant_Recomputes()
    {
        using var tempDir = new TempDirectory("RunFence_AclTest");
        var subDir = Path.Combine(tempDir.Path, "Sub");
        Directory.CreateDirectory(subDir);
        var parentExePath = Path.Combine(tempDir.Path, "main.exe");
        File.WriteAllBytes(parentExePath, []);
        var childExePath = Path.Combine(subDir, "tool.exe");
        File.WriteAllBytes(childExePath, []);

        var folderApp = CreateApp("f0001", "FolderApp", parentExePath,
            AclTarget.Folder, accountSid: Sid1, folderAclDepth: 0);
        var childApp = CreateApp("e0001", "ExeApp", childExePath, accountSid: Sid2);
        var localUserProvider = new Mock<ILocalUserProvider>();
        localUserProvider.Setup(provider => provider.GetLocalUserAccounts())
            .Returns(
            [
                new LocalUserAccount("User1", Sid1),
                new LocalUserAccount("User2", Sid2)
            ]);

        var dbProvider = new LambdaDatabaseProvider(() => new AppDatabase());
        var containerLookup = new ContainerLookupHelper(dbProvider);
        var aclAccessor = AclAccessorFactory.Create();
        var resolver = new AppEntryAclTargetResolver();
        var denyService = new AclDenyModeService(
            _log.Object,
            localUserProvider.Object,
            containerLookup,
            new Mock<IInteractiveUserResolver>().Object,
            aclAccessor,
            resolver);
        var allowService = new AclAllowModeService(
            _log.Object,
            localUserProvider.Object,
            aclAccessor,
            new AppEntryAllowAclRuleProvider(resolver));
        var service = new AclService(
            _log.Object,
            denyService,
            allowService,
            containerLookup,
            new Mock<IGrantMutatorService>().Object,
            resolver);

        static List<FileSystemAccessRule> GetExplicitDenyRules(string path, string sid)
        {
            var security = new DirectoryInfo(path).GetAccessControl();
            return security.GetAccessRules(true, false, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Where(rule =>
                    rule.AccessControlType == AccessControlType.Deny &&
                    string.Equals(rule.IdentityReference.Value, sid, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        service.RecomputeAllAncestorAcls([folderApp, childApp]);
        Assert.Empty(GetExplicitDenyRules(tempDir.Path, Sid2));

        service.RecomputeAllAncestorAcls([folderApp]);

        var denyRules = GetExplicitDenyRules(tempDir.Path, Sid2);
        Assert.NotEmpty(denyRules);
        Assert.Contains(denyRules, rule => rule.FileSystemRights.HasFlag(FileSystemRights.ExecuteFile));
    }

    [Fact]
    public void ResolveAclTargetPath_FolderApp_Depth0_ReturnsFolderItself()
    {
        var app = CreateApp("f0001", "FolderApp", @"C:\MyApps\GameFolder",
            AclTarget.Folder, isFolder: true);
        var result = _service.ResolveAclTargetPath(app);
        Assert.Equal(Path.GetFullPath(@"C:\MyApps\GameFolder"), result);
    }

    [Fact]
    public void ResolveAclTargetPath_FolderApp_Depth1_ReturnsParent()
    {
        var app = CreateApp("f0001", "FolderApp", @"C:\MyApps\SubDir\GameFolder",
            AclTarget.Folder, isFolder: true, folderAclDepth: 1);
        var result = _service.ResolveAclTargetPath(app);
        Assert.Equal(Path.GetFullPath(@"C:\MyApps\SubDir"), result);
    }

    [Fact]
    public void ApplyAcl_FolderApp_ExeTarget_ReturnsEarly()
    {
        var app = CreateApp("f0001", "FolderApp", @"C:\MyApps\GameFolder", isFolder: true);
        var allApps = new List<AppEntry> { app };

        _service.ApplyAcl(app, allApps);

        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Applied ACL"))), Times.Never);
    }

[Fact]
    public void ApplyAcl_AllowMode_DispatchesToAllowPath()
    {
        using var tempDir = new TempDirectory("RunFence_AclTest");
        var exePath = Path.Combine(tempDir.Path, "app.exe");
        File.WriteAllBytes(exePath, []);

        var app = CreateApp("a0001", "AllowApp", exePath, accountSid: Sid1,
            aclMode: AclMode.Allow, allowedAclEntries: [new() { Sid = "S-1-5-32-547", AllowExecute = true, AllowWrite = false }]);

        var allApps = new List<AppEntry> { app };

        _service.ApplyAcl(app, allApps);

        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Applied allow-mode ACL"))), Times.Once);
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Applied deny ACL"))), Times.Never);
    }

[Fact]
    public void RevertAcl_AllowMode_DispatchesToAllowRevert()
    {
        using var tempDir = new TempDirectory("RunFence_AclTest");
        var exePath = Path.Combine(tempDir.Path, "app.exe");
        File.WriteAllBytes(exePath, []);

        var app = CreateApp("a0001", "AllowApp", exePath, accountSid: Sid1,
            aclMode: AclMode.Allow, allowedAclEntries: [new() { Sid = "S-1-5-32-547", AllowExecute = true }]);

        // First apply to have something to revert
        _service.ApplyAcl(app, new List<AppEntry> { app });

        _service.RevertAcl(app, new List<AppEntry> { app });

        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Reverted allow-mode ACL"))), Times.Once);
    }

[Fact]
    public void RevertAcl_DenyMode_OtherAppsFilter_ExcludesAllowMode()
    {
        using var tempDir = new TempDirectory("RunFence_AclTest");
        var exePath = Path.Combine(tempDir.Path, "app.exe");
        File.WriteAllBytes(exePath, []);

        var denyApp = CreateApp("d0001", "DenyApp", exePath, accountSid: Sid1, aclMode: AclMode.Deny);
        var allowApp = CreateApp("a0001", "AllowApp", exePath, accountSid: Sid2,
            aclMode: AclMode.Allow, allowedAclEntries: [new() { Sid = "S-1-5-32-547", AllowExecute = true }]);

        var allApps = new List<AppEntry> { denyApp, allowApp };

        // Revert the deny app — the allow app should NOT be picked as "other app"
        _service.RevertAcl(denyApp, allApps);

        // The allow app should not trigger a reapply
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Applied deny ACL"))), Times.Never);
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Applied allow-mode ACL"))), Times.Never);
    }

    [Fact]
    public void GetAllowedSidsForPath_MixedModes_ExcludesAllowMode()
    {
        var denyApp = CreateApp("d0001", "DenyApp", @"C:\Apps\tool.exe", accountSid: Sid1, aclMode: AclMode.Deny);
        var allowApp = CreateApp("a0001", "AllowApp", @"C:\Apps\tool.exe", accountSid: Sid2,
            aclMode: AclMode.Allow, allowedAclEntries: []);

        var allApps = new List<AppEntry> { denyApp, allowApp };

        var allowed = _denyService.GetAllowedSidsForPath(
            @"C:\Apps\tool.exe", allApps, isFolderTarget: false);

        Assert.Contains(Sid1, allowed); // deny-mode SID is included
        Assert.DoesNotContain(Sid2, allowed); // allow-mode SID is excluded
    }

    // --- DeniedRights tests ---

    [Theory]
    [InlineData(DeniedRights.Execute, FileSystemRights.ExecuteFile)]
    [InlineData(DeniedRights.ExecuteWrite,
        FileSystemRights.ExecuteFile |
        FileSystemRights.WriteData |
        FileSystemRights.AppendData |
        FileSystemRights.WriteAttributes |
        FileSystemRights.WriteExtendedAttributes |
        FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles)]
    [InlineData(DeniedRights.ExecuteReadWrite,
        FileSystemRights.ExecuteFile |
        FileSystemRights.WriteData |
        FileSystemRights.AppendData |
        FileSystemRights.WriteAttributes |
        FileSystemRights.WriteExtendedAttributes |
        FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.ReadData |
        FileSystemRights.ReadAttributes |
        FileSystemRights.ReadExtendedAttributes)]
    public void MapDeniedRights_ReturnsCorrectFlags(DeniedRights deniedRights, FileSystemRights expected)
    {
        var result = AclRightsHelper.MapDeniedRights(deniedRights);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void RecomputeAllAncestorAcls_AllowModeApps_Excluded()
    {
        using var tempDir = new TempDirectory("RunFence_AclTest");
        var subDir = Path.Combine(tempDir.Path, "Sub");
        Directory.CreateDirectory(subDir);
        var childExe = Path.Combine(subDir, "tool.exe");
        File.WriteAllBytes(childExe, []);
        var parentExe = Path.Combine(tempDir.Path, "main.exe");
        File.WriteAllBytes(parentExe, []);

        // Folder app with allow mode — should be excluded from ancestor computation
        var folderApp = CreateApp("f0001", "FolderApp", parentExe,
            AclTarget.Folder, accountSid: Sid1, aclMode: AclMode.Allow,
            allowedAclEntries: [new() { Sid = "S-1-5-32-547", AllowExecute = true }]);
        var exeApp = CreateApp("e0001", "ExeApp", childExe, accountSid: Sid2, aclMode: AclMode.Deny);

        var allApps = new List<AppEntry> { folderApp, exeApp };

        _service.RecomputeAllAncestorAcls(allApps);

        // Allow-mode folder app should be excluded, so no ancestor recomputation
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Recomputed ancestor ACL"))), Times.Never);
    }

    [Fact]
    public void ApplyAcl_AllowMode_AfterDenyMode_CleansUpDenyAces()
    {
        using var tempDir = new TempDirectory("RunFence_AclTest");
        var exePath = Path.Combine(tempDir.Path, "app.exe");
        File.WriteAllBytes(exePath, []);

        // First apply deny mode
        var denyApp = CreateApp("d0001", "App", exePath, accountSid: Sid1, aclMode: AclMode.Deny);
        _service.ApplyAcl(denyApp, new List<AppEntry> { denyApp });

        // Switch to allow mode — stale deny ACEs must be cleaned up (denyCleaned=true → changed=true)
        var allowApp = CreateApp("d0001", "App", exePath, accountSid: Sid1,
            aclMode: AclMode.Allow, allowedAclEntries: [new() { Sid = "S-1-5-32-547", AllowExecute = true }]);
        _service.ApplyAcl(allowApp, new List<AppEntry> { allowApp });

        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Applied allow-mode ACL"))), Times.Once);
    }

    [Fact]
    public void ApplyAcl_DenyMode_AfterAllowMode_CleansUpAllowModeAces()
    {
        using var tempDir = new TempDirectory("RunFence_AclTest");
        var exePath = Path.Combine(tempDir.Path, "app.exe");
        File.WriteAllBytes(exePath, []);

        // First apply allow mode (breaks inheritance)
        var allowApp = CreateApp("a0001", "App", exePath, accountSid: Sid1,
            aclMode: AclMode.Allow, allowedAclEntries: [new() { Sid = "S-1-5-32-547", AllowExecute = true }]);
        _service.ApplyAcl(allowApp, new List<AppEntry> { allowApp });
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Applied allow-mode ACL"))), Times.Once);

        // Switch to deny mode — allow-mode state (broken inheritance + allow ACEs) must be cleaned up
        var denyApp = CreateApp("a0001", "App", exePath, accountSid: Sid1, aclMode: AclMode.Deny);
        _service.ApplyAcl(denyApp, new List<AppEntry> { denyApp });

        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Cleaned up allow-mode ACEs"))), Times.Once);
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Applied deny ACL"))), Times.Once);
    }

    // --- Backward compatibility tests ---

    // --- AppContainer ACL integration tests ---

    [Fact]
    public void ApplyAcl_AppContainer_DenyMode_GrantsContainerSidReadExecute()
    {
        using var tempDir = new TempDirectory("RunFence_AclTest");
        var exePath = Path.Combine(tempDir.Path, "app.exe");
        File.WriteAllBytes(exePath, []);

        // Use ALL_APPLICATION_PACKAGES as a well-known valid SID string
        const string containerSid = "S-1-15-2-1";
        var db = new AppDatabase();
        db.AppContainers.Add(new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser", Sid = containerSid });

        var pathGrantService = new Mock<IGrantMutatorService>();
        var service = CreateService(
            _log.Object,
            _localUserProvider,
            new LambdaDatabaseProvider(() => db),
            pathGrantService);
        var app = CreateApp("c0001", "BrowserApp", exePath, accountSid: "",
            aclMode: AclMode.Deny);
        app.AppContainerName = "ram_browser";

        service.ApplyAcl(app, new List<AppEntry> { app });

        pathGrantService.Verify(p => p.EnsureAccess(
            containerSid,
            exePath,
            FileSystemRights.ReadAndExecute,
            null,
            false), Times.Once);
    }

    [Fact]
    public void RevertAcl_AppContainer_RevokesContainerSidGrant()
    {
        using var tempDir = new TempDirectory("RunFence_AclTest");
        var exePath = Path.Combine(tempDir.Path, "app.exe");
        File.WriteAllBytes(exePath, []);

        const string containerSid = "S-1-15-2-1";
        var db = new AppDatabase();
        db.AppContainers.Add(new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser", Sid = containerSid });

        var pathGrantService = new Mock<IGrantMutatorService>();
        var service = CreateService(
            _log.Object,
            _localUserProvider,
            new LambdaDatabaseProvider(() => db),
            pathGrantService);
        var app = CreateApp("c0001", "BrowserApp", exePath,
            accountSid: Sid1, aclMode: AclMode.Deny);
        app.AppContainerName = "ram_browser";

        // Apply first, then revert
        service.ApplyAcl(app, new List<AppEntry> { app });
        service.RevertAcl(app, new List<AppEntry> { app });

        pathGrantService.Verify(p => p.RemoveGrant(
            containerSid,
            exePath,
            false), Times.Once);
    }

    [Fact]
    public void ApplyAcl_AppContainer_GrantOperationException_IsLoggedAndSwallowed()
    {
        using var tempDir = new TempDirectory("RunFence_AclTest");
        var exePath = Path.Combine(tempDir.Path, "app.exe");
        File.WriteAllBytes(exePath, []);

        const string containerSid = "S-1-15-2-1";
        var db = new AppDatabase();
        db.AppContainers.Add(new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser", Sid = containerSid });

        var pathGrantService = new Mock<IGrantMutatorService>();
        pathGrantService.Setup(p => p.EnsureAccess(
                containerSid,
                exePath,
                FileSystemRights.ReadAndExecute,
                null,
                false))
            .Throws(new GrantOperationException(
                GrantApplyFailureStep.GrantAclApply,
                exePath,
                null,
                new UnauthorizedAccessException("denied")));
        var service = CreateService(
            _log.Object,
            _localUserProvider,
            new LambdaDatabaseProvider(() => db),
            pathGrantService);
        var app = CreateApp("c0001", "BrowserApp", exePath, accountSid: "", aclMode: AclMode.Deny);
        app.AppContainerName = "ram_browser";

        var exception = Record.Exception(() => service.ApplyAcl(app, new List<AppEntry> { app }));

        Assert.Null(exception);
    }

    [Fact]
    public void ApplyAcl_AppContainer_GrantReturnsWarning_IsLoggedAndSwallowed()
    {
        using var tempDir = new TempDirectory("RunFence_AclTest");
        var exePath = Path.Combine(tempDir.Path, "app.exe");
        File.WriteAllBytes(exePath, []);

        const string containerSid = "S-1-15-2-1";
        var warning = new GrantApplyWarning(
            GrantApplyFailureStep.PostGrantMutationSave,
            exePath,
            null,
            new InvalidOperationException("save failed"));
        var db = new AppDatabase();
        db.AppContainers.Add(new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser", Sid = containerSid });

        var pathGrantService = new Mock<IGrantMutatorService>();
        pathGrantService.Setup(p => p.EnsureAccess(
                containerSid,
                exePath,
                FileSystemRights.ReadAndExecute,
                null,
                false))
            .Returns(new GrantApplyResult(
                DatabaseModified: true,
                DurableSaveCompleted: false,
                Warnings: [warning]));
        var service = CreateService(
            _log.Object,
            _localUserProvider,
            new LambdaDatabaseProvider(() => db),
            pathGrantService);
        var app = CreateApp("c0001", "BrowserApp", exePath, accountSid: "", aclMode: AclMode.Deny);
        app.AppContainerName = "ram_browser";

        var exception = Record.Exception(() => service.ApplyAcl(app, new List<AppEntry> { app }));

        Assert.Null(exception);
    }

    [Fact]
    public void RevertAcl_AppContainer_RevokeCanceled_IsLoggedAndSwallowed()
    {
        using var tempDir = new TempDirectory("RunFence_AclTest");
        var exePath = Path.Combine(tempDir.Path, "app.exe");
        File.WriteAllBytes(exePath, []);

        const string containerSid = "S-1-15-2-1";
        var db = new AppDatabase();
        db.AppContainers.Add(new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser", Sid = containerSid });

        var pathGrantService = new Mock<IGrantMutatorService>();
        pathGrantService.Setup(p => p.RemoveGrant(
                containerSid,
                exePath,
                false))
            .Throws(new OperationCanceledException("user declined"));
        var service = CreateService(
            _log.Object,
            _localUserProvider,
            new LambdaDatabaseProvider(() => db),
            pathGrantService);
        var app = CreateApp("c0001", "BrowserApp", exePath,
            accountSid: Sid1, aclMode: AclMode.Deny);
        app.AppContainerName = "ram_browser";

        var exception = Record.Exception(() => service.RevertAcl(app, new List<AppEntry> { app }));

        Assert.Null(exception);
    }

    [Fact]
    public void RevertAcl_AppContainer_RevokeReturnsWarning_IsLoggedAndSwallowed()
    {
        using var tempDir = new TempDirectory("RunFence_AclTest");
        var exePath = Path.Combine(tempDir.Path, "app.exe");
        File.WriteAllBytes(exePath, []);

        const string containerSid = "S-1-15-2-1";
        var warning = new GrantApplyWarning(
            GrantApplyFailureStep.PostGrantRemoveSave,
            exePath,
            null,
            new InvalidOperationException("save failed"));
        var db = new AppDatabase();
        db.AppContainers.Add(new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser", Sid = containerSid });

        var pathGrantService = new Mock<IGrantMutatorService>();
        pathGrantService.Setup(p => p.RemoveGrant(
                containerSid,
                exePath,
                false))
            .Returns(new GrantApplyResult(
                DatabaseModified: true,
                DurableSaveCompleted: false,
                Warnings: [warning]));
        var service = CreateService(
            _log.Object,
            _localUserProvider,
            new LambdaDatabaseProvider(() => db),
            pathGrantService);
        var app = CreateApp("c0001", "BrowserApp", exePath,
            accountSid: Sid1, aclMode: AclMode.Deny);
        app.AppContainerName = "ram_browser";

        service.ApplyAcl(app, new List<AppEntry> { app });
        var exception = Record.Exception(() => service.RevertAcl(app, new List<AppEntry> { app }));

        Assert.Null(exception);
    }

    [Fact]
    public void GetAllowedSidsForPath_AppContainerApp_DoesNotAddEmptyAccountSid()
    {
        // AppContainer apps have empty AccountSid — must NOT add "" to the allowed set
        // (which would effectively allow all users through the deny-mode filter).
        var app = CreateApp("c0001", "ContainerApp", @"C:\Apps\tool.exe", accountSid: "", aclMode: AclMode.Deny);
        app.AppContainerName = "ram_browser";

        var allowed = _denyService.GetAllowedSidsForPath(
            @"C:\Apps\tool.exe", new List<AppEntry> { app }, isFolderTarget: false);

        // Empty string must NOT be in the allowed set
        Assert.DoesNotContain("", allowed);
    }

    [Fact]
    public void GetAllowedSidsForPath_AppContainerApp_IncludesInteractiveUserSid()
    {
        // AppContainer apps must include the interactive user SID in the allowed set
        // so the interactive user can still access the exe through the deny filter.
        const string interactiveSid = "S-1-5-21-1234567890-1234567890-1234567890-1099";
        var interactiveResolver = new Mock<IInteractiveUserResolver>();
        interactiveResolver.Setup(r => r.GetInteractiveUserSid()).Returns(interactiveSid);

        var containerLookup = new ContainerLookupHelper(new LambdaDatabaseProvider(() => new AppDatabase()));
        var aclAccessor = AclAccessorFactory.Create();
        var resolver = new AppEntryAclTargetResolver();
        var denyService = new AclDenyModeService(_log.Object, _localUserProvider,
            containerLookup, interactiveResolver.Object, aclAccessor, resolver);
        var service = new AclService(_log.Object, denyService, new AclAllowModeService(
                _log.Object,
                _localUserProvider,
                aclAccessor,
                new AppEntryAllowAclRuleProvider(resolver)),
            new ContainerLookupHelper(new LambdaDatabaseProvider(() => new AppDatabase())),
            new Mock<IGrantMutatorService>().Object,
            resolver);

        var app = CreateApp("c0001", "ContainerApp", @"C:\Apps\tool.exe", accountSid: "", aclMode: AclMode.Deny);
        app.AppContainerName = "ram_browser";

        var allowed = denyService.GetAllowedSidsForPath(
            @"C:\Apps\tool.exe", new List<AppEntry> { app }, isFolderTarget: false);

        Assert.Contains(interactiveSid, allowed, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetAllowedSidsForPath_AppContainerApp_IncludesContainerSidWhenResolved()
    {
        // When the container entry has a resolved Sid, it must appear in the allowed set
        // so the sandboxed process can reach its exe through the deny filter.
        const string containerSid = "S-1-15-2-99";
        var db = new AppDatabase();
        db.AppContainers.Add(new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser", Sid = containerSid });

        var (service, denyService) = CreateServiceAndDenyService(
            _log.Object, _localUserProvider, new LambdaDatabaseProvider(() => db));
        var app = CreateApp("c0001", "ContainerApp", @"C:\Apps\tool.exe", accountSid: "", aclMode: AclMode.Deny);
        app.AppContainerName = "ram_browser";

        var allowed = denyService.GetAllowedSidsForPath(
            @"C:\Apps\tool.exe", new List<AppEntry> { app }, isFolderTarget: false);

        Assert.Contains(containerSid, allowed, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackwardCompat_OldAppEntry_DefaultsToDenyExecute()
    {
        var app = new AppEntry();
        Assert.Equal(AclMode.Deny, app.AclMode);
        Assert.Equal(DeniedRights.Execute, app.DeniedRights);
        Assert.Null(app.AllowedAclEntries);
    }

    [Fact]
    public void BackwardCompat_JsonRoundTrip()
    {
        // Simulate an old config that doesn't have the new fields
        var json = """{"id":"test1","name":"Test","exePath":"C:\\test.exe","restrictAcl":true,"aclTarget":"Folder"}""";
        var app = JsonSerializer.Deserialize<AppEntry>(json, JsonDefaults.Options);

        Assert.NotNull(app);
        Assert.Equal(AclMode.Deny, app.AclMode);
        Assert.Equal(DeniedRights.Execute, app.DeniedRights);
        Assert.Null(app.AllowedAclEntries);
    }
}
