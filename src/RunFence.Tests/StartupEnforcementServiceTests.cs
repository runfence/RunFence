using Moq;
using RunFence.Acl;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;
using RunFence.Startup;
using Xunit;

namespace RunFence.Tests;

public class StartupEnforcementServiceTests : IDisposable
{
    private static readonly SidDisplayNameResolver DefaultDisplayNameResolver =
        new(new Mock<ISidResolver>().Object);

    private readonly Mock<IAclService> _aclService;
    private readonly Mock<IShortcutService> _shortcutService;
    private readonly Mock<IBesideTargetShortcutService> _besideTargetShortcutService;
    private readonly Mock<IShortcutDiscoveryService> _shortcutDiscovery;
    private readonly Mock<IIconService> _iconService;
    private readonly Mock<ILoggingService> _log;
    private readonly StartupEnforcementService _service;
    private readonly string _fakeExePath;
    private readonly TempDirectory _tempDir;

    public StartupEnforcementServiceTests()
    {
        _aclService = new Mock<IAclService>();
        _shortcutService = new Mock<IShortcutService>();
        _besideTargetShortcutService = new Mock<IBesideTargetShortcutService>();
        _shortcutDiscovery = new Mock<IShortcutDiscoveryService>();
        _shortcutDiscovery.Setup(d => d.CreateTraversalCache()).Returns(() => new ShortcutTraversalCache([]));
        _iconService = new Mock<IIconService>();
        _log = new Mock<ILoggingService>();
        var appContainerService = new Mock<IAppContainerService>();
        _service = new StartupEnforcementService(
            _aclService.Object, _shortcutService.Object,
            _besideTargetShortcutService.Object,
            _shortcutDiscovery.Object,
            _iconService.Object, _log.Object,
            DefaultDisplayNameResolver,
            appContainerService.Object);

        _tempDir = new TempDirectory("RunFence_EnforcementTest");

        // Create a fake exe for testing non-URL apps that need to exist
        _fakeExePath = Path.Combine(_tempDir.Path, "testapp.exe");
        File.WriteAllBytes(_fakeExePath, []);
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    /// <summary>
    /// Sets up the shortcut service mock to capture the resolver callback, calls Enforce,
    /// asserts the resolver was captured, and returns it for further assertion by the caller.
    /// </summary>
    private Func<AppEntry, (string, string)?> CaptureShortcutResolver(AppDatabase db)
    {
        Func<AppEntry, (string, string)?>? capturedResolver = null;
        _besideTargetShortcutService
            .Setup(s => s.EnforceBesideTargetShortcuts(
                It.IsAny<IEnumerable<AppEntry>>(),
                It.IsAny<string>(),
                It.IsAny<Func<AppEntry, (string, string)?>>()))
            .Callback<IEnumerable<AppEntry>, string, Func<AppEntry, (string, string)?>>((_, _, resolver) => capturedResolver = resolver);

        _service.Enforce(db);

        Assert.NotNull(capturedResolver);
        return capturedResolver!;
    }

    [Fact]
    public void Enforce_EmptyDatabase_NoErrors()
    {
        var db = new AppDatabase();
        _service.Enforce(db);
        _aclService.Verify(a => a.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
    }

    [Fact]
    public void Enforce_NonExistentExe_StillAppliesAcl()
    {
        // Target existence is intentionally NOT checked — the admin may be denied read access
        // on a path whose ACL is being enforced; skipping in that case would defeat the purpose.
        var app = new AppEntry
        {
            Name = "MissingApp",
            IsUrlScheme = false,
            ExePath = @"C:\nonexistent\app.exe",
            RestrictAcl = true,
            ManageShortcuts = false
        };
        var db = new AppDatabase { Apps = [app] };

        _service.Enforce(db);

        _aclService.Verify(a => a.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }

    [Fact]
    public void Enforce_NoIconRegeneration_ReturnsEmptyTimestamps()
    {
        // URL scheme apps skip icon checks entirely
        var app = new AppEntry
        {
            Name = "UrlApp",
            IsUrlScheme = true,
            ExePath = "steam://run/123",
            ManageShortcuts = false,
            RestrictAcl = false
        };
        var db = new AppDatabase { Apps = [app] };

        var result = _service.Enforce(db);

        _iconService.Verify(i => i.NeedsRegeneration(It.IsAny<AppEntry>()), Times.Never);
        Assert.Empty(result.TimestampUpdates);
        Assert.Empty(result.TraverseGrants);
    }

    [Fact]
    public void Enforce_IconRegeneration_ReturnsTimestampUpdate()
    {
        var app = new AppEntry
        {
            Id = "abc12",
            Name = "RegenApp",
            IsUrlScheme = false,
            ExePath = _fakeExePath,
            RestrictAcl = false,
            ManageShortcuts = false
        };
        _iconService.Setup(i => i.NeedsRegeneration(app)).Returns(true);
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Returns("icon.ico");

        var db = new AppDatabase { Apps = [app] };

        var result = _service.Enforce(db);

        _iconService.Verify(i => i.CreateBadgedIcon(app, null), Times.Once);
        Assert.Single(result.TimestampUpdates);
        Assert.True(result.TimestampUpdates.ContainsKey("abc12"));
        Assert.Empty(result.TraverseGrants);
    }

    [Fact]
    public void Enforce_IconRegenerationFails_ReturnsEmptyTimestamps()
    {
        var app = new AppEntry
        {
            Name = "RegenFailApp",
            IsUrlScheme = false,
            ExePath = _fakeExePath,
            RestrictAcl = false,
            ManageShortcuts = false
        };
        _iconService.Setup(i => i.NeedsRegeneration(app)).Returns(true);
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Throws(new IOException("Test icon failure"));

        var db = new AppDatabase { Apps = [app] };

        var result = _service.Enforce(db);

        _log.Verify(l => l.Error(It.Is<string>(s => s.Contains("Icon regeneration failed")), It.IsAny<Exception>()), Times.Once);
        Assert.Empty(result.TimestampUpdates);
        Assert.Empty(result.TraverseGrants);
    }

    // --- ManageShortcuts path tests ---

    [Fact]
    public void Enforce_ManageShortcutsTrue_LauncherExists_CallsEnforceShortcuts()
    {
        var app = new AppEntry
        {
            Name = "ShortcutApp",
            IsUrlScheme = false,
            ExePath = _fakeExePath,
            RestrictAcl = false,
            ManageShortcuts = true
        };
        _iconService.Setup(i => i.NeedsRegeneration(It.IsAny<AppEntry>())).Returns(false);
        var db = new AppDatabase { Apps = [app] };
        var cache = new ShortcutTraversalCache([]);
        _shortcutDiscovery.Setup(d => d.CreateTraversalCache()).Returns(cache);

        _service.Enforce(db);

        _shortcutService.Verify(s => s.EnforceShortcuts(
            It.Is<IEnumerable<AppEntry>>(apps => apps.Contains(app)),
            It.IsAny<string>(),
            cache), Times.Once);
        _shortcutDiscovery.Verify(d => d.CreateTraversalCache(), Times.Once);
    }

    [Fact]
    public void Enforce_ManageShortcutsFalse_DoesNotCallEnforceShortcuts()
    {
        var app = new AppEntry
        {
            Name = "NoShortcutApp",
            IsUrlScheme = false,
            ExePath = _fakeExePath,
            RestrictAcl = false,
            ManageShortcuts = false
        };
        _iconService.Setup(i => i.NeedsRegeneration(It.IsAny<AppEntry>())).Returns(false);
        var db = new AppDatabase { Apps = [app] };

        _service.Enforce(db);

        _shortcutService.Verify(s => s.EnforceShortcuts(
            It.IsAny<IEnumerable<AppEntry>>(),
            It.IsAny<string>(),
            It.IsAny<ShortcutTraversalCache>()), Times.Never);
        _shortcutDiscovery.Verify(d => d.CreateTraversalCache(), Times.Never);
    }

    [Fact]
    public void Enforce_UrlSchemeApp_ManageShortcutsTrue_LauncherExists_CallsEnforceShortcuts()
    {
        // URL scheme apps can have ManageShortcuts = true and should still get shortcuts managed
        var app = new AppEntry
        {
            Name = "UrlShortcutApp",
            IsUrlScheme = true,
            ExePath = "steam://run/123",
            RestrictAcl = false,
            ManageShortcuts = true
        };
        var db = new AppDatabase { Apps = [app] };

        _service.Enforce(db);

        _shortcutService.Verify(s => s.EnforceShortcuts(
            It.Is<IEnumerable<AppEntry>>(apps => apps.Contains(app)),
            It.IsAny<string>(),
            It.IsAny<ShortcutTraversalCache>()), Times.Once);
    }

    // --- Multiple apps with mixed settings ---

    [Fact]
    public void Enforce_MultipleApps_MixedSettings_CorrectDispatch()
    {
        var aclApp = new AppEntry
        {
            Name = "AclApp",
            IsUrlScheme = false,
            ExePath = _fakeExePath,
            RestrictAcl = true,
            ManageShortcuts = false
        };
        var urlApp = new AppEntry
        {
            Name = "UrlApp",
            IsUrlScheme = true,
            ExePath = "steam://run/123",
            RestrictAcl = true, // Should be skipped due to IsUrlScheme
            ManageShortcuts = false
        };
        var shortcutApp = new AppEntry
        {
            Name = "ShortcutApp",
            IsUrlScheme = false,
            ExePath = _fakeExePath,
            RestrictAcl = false,
            ManageShortcuts = true
        };
        var missingApp = new AppEntry
        {
            Name = "MissingApp",
            IsUrlScheme = false,
            ExePath = @"C:\nonexistent\missing.exe",
            RestrictAcl = true,
            ManageShortcuts = true
        };

        _iconService.Setup(i => i.NeedsRegeneration(It.IsAny<AppEntry>())).Returns(false);
        var db = new AppDatabase { Apps = [aclApp, urlApp, shortcutApp, missingApp] };

        _service.Enforce(db);

        // aclApp and missingApp both get ApplyAcl (existence not checked); urlApp is skipped (IsUrlScheme)
        _aclService.Verify(a => a.ApplyAcl(
            It.Is<AppEntry>(app => app.Name == "AclApp"),
            It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
        _aclService.Verify(a => a.ApplyAcl(
            It.Is<AppEntry>(app => app.Name == "MissingApp"),
            It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
        _aclService.Verify(a => a.ApplyAcl(
            It.Is<AppEntry>(app => app.Name == "UrlApp"),
            It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);

        // shortcutApp should have EnforceShortcuts called
        _shortcutService.Verify(s => s.EnforceShortcuts(
            It.Is<IEnumerable<AppEntry>>(apps => apps.Any(a => a.Name == "ShortcutApp")),
            It.IsAny<string>(),
            It.IsAny<ShortcutTraversalCache>()), Times.Once);
    }

    [Fact]
    public void Enforce_MultipleAppsWithAcl_AllGetAclApplied()
    {
        // Create a second fake exe
        var fakeExe2 = Path.Combine(_tempDir.Path, "testapp2.exe");
        File.WriteAllBytes(fakeExe2, []);

        var app1 = new AppEntry
        {
            Name = "AclApp1",
            IsUrlScheme = false,
            ExePath = _fakeExePath,
            RestrictAcl = true,
            ManageShortcuts = false
        };
        var app2 = new AppEntry
        {
            Name = "AclApp2",
            IsUrlScheme = false,
            ExePath = fakeExe2,
            RestrictAcl = true,
            ManageShortcuts = false
        };

        _iconService.Setup(i => i.NeedsRegeneration(It.IsAny<AppEntry>())).Returns(false);
        var db = new AppDatabase { Apps = [app1, app2] };

        _service.Enforce(db);

        // Both apps should get ACL applied
        _aclService.Verify(a => a.ApplyAcl(
            It.IsAny<AppEntry>(),
            It.IsAny<IReadOnlyList<AppEntry>>()), Times.Exactly(2));
    }

    [Fact]
    public void Enforce_AclExceptionCaught_ContinuesWithNextApp()
    {
        var failApp = new AppEntry
        {
            Name = "FailApp",
            IsUrlScheme = false,
            ExePath = _fakeExePath,
            RestrictAcl = true,
            ManageShortcuts = false
        };
        var fakeExe2 = Path.Combine(_tempDir.Path, "testapp2.exe");
        File.WriteAllBytes(fakeExe2, []);
        var okApp = new AppEntry
        {
            Name = "OkApp",
            IsUrlScheme = false,
            ExePath = fakeExe2,
            RestrictAcl = true,
            ManageShortcuts = false
        };

        _iconService.Setup(i => i.NeedsRegeneration(It.IsAny<AppEntry>())).Returns(false);

        // First app's ACL throws, second succeeds
        _aclService.Setup(a => a.ApplyAcl(
                It.Is<AppEntry>(app => app.Name == "FailApp"),
                It.IsAny<IReadOnlyList<AppEntry>>()))
            .Throws(new UnauthorizedAccessException("Test ACL failure"));

        var db = new AppDatabase { Apps = [failApp, okApp] };

        _service.Enforce(db);

        // Should log error for FailApp
        _log.Verify(l => l.Error(
            It.Is<string>(s => s.Contains("ACL enforcement failed") && s.Contains("FailApp")),
            It.IsAny<Exception>()), Times.Once);

        // Should still call ApplyAcl for OkApp
        _aclService.Verify(a => a.ApplyAcl(
            It.Is<AppEntry>(app => app.Name == "OkApp"),
            It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }

    [Fact]
    public void Enforce_ShortcutExceptionCaught_ContinuesProcessing()
    {
        var app = new AppEntry
        {
            Name = "ShortcutFailApp",
            IsUrlScheme = false,
            ExePath = _fakeExePath,
            RestrictAcl = false,
            ManageShortcuts = true
        };

        _iconService.Setup(i => i.NeedsRegeneration(It.IsAny<AppEntry>())).Returns(false);
        _shortcutService.Setup(s => s.EnforceShortcuts(
                It.IsAny<IEnumerable<AppEntry>>(),
                It.IsAny<string>(),
                It.IsAny<ShortcutTraversalCache>()))
            .Throws(new IOException("Test shortcut failure"));

        var db = new AppDatabase { Apps = [app] };

        // Should not throw — exception is caught internally
        _service.Enforce(db);

        _log.Verify(l => l.Error(
            It.Is<string>(s => s.Contains("Shortcut enforcement failed")),
            It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void Enforce_CallsRecomputeAllAncestorAcls()
    {
        var db = new AppDatabase();

        _service.Enforce(db);

        _aclService.Verify(a => a.RecomputeAllAncestorAcls(
            db.Apps), Times.Once);
    }

    [Fact]
    public void Enforce_RecomputeAncestorAclsFails_DoesNotThrow()
    {
        _aclService.Setup(a => a.RecomputeAllAncestorAcls(
                It.IsAny<IReadOnlyList<AppEntry>>()))
            .Throws(new UnauthorizedAccessException("Test failure"));

        var db = new AppDatabase();

        _service.Enforce(db);

        _log.Verify(l => l.Error(
            It.Is<string>(s => s.Contains("Ancestor ACL recomputation failed")),
            It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void Enforce_RestrictAclFalse_ExeExists_SkipsAclButChecksIcons()
    {
        var app = new AppEntry
        {
            Name = "NoAclApp",
            IsUrlScheme = false,
            ExePath = _fakeExePath,
            RestrictAcl = false,
            ManageShortcuts = false
        };

        _iconService.Setup(i => i.NeedsRegeneration(It.IsAny<AppEntry>())).Returns(false);
        var db = new AppDatabase { Apps = [app] };

        _service.Enforce(db);

        // ACL should not be applied
        _aclService.Verify(a => a.ApplyAcl(
            It.IsAny<AppEntry>(),
            It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);

        // Icon check should still happen for non-URL apps
        _iconService.Verify(i => i.NeedsRegeneration(app), Times.Once);
    }

    // --- ACL lockout enforcement scenarios ---

    [Theory]
    [InlineData(true, false, true)]   // RestrictAcl=true, IsUrlScheme=false → ApplyAcl called
    [InlineData(true, true, false)]   // RestrictAcl=true, IsUrlScheme=true  → ApplyAcl skipped (url scheme)
    [InlineData(false, false, false)] // RestrictAcl=false, IsUrlScheme=false → ApplyAcl not called
    [InlineData(false, true, false)]  // RestrictAcl=false, IsUrlScheme=true  → ApplyAcl not called
    public void Enforce_AclLockoutScenarios_AclAppliedOnlyWhenRestricted(
        bool restrictAcl, bool isUrlScheme, bool expectAclApplied)
    {
        var app = new AppEntry
        {
            Name = "LockoutScenarioApp",
            IsUrlScheme = isUrlScheme,
            ExePath = isUrlScheme ? "steam://run/123" : _fakeExePath,
            RestrictAcl = restrictAcl,
            ManageShortcuts = false
        };
        _iconService.Setup(i => i.NeedsRegeneration(It.IsAny<AppEntry>())).Returns(false);
        var db = new AppDatabase { Apps = [app] };

        _service.Enforce(db);

        _aclService.Verify(a => a.ApplyAcl(
            It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()),
            expectAclApplied ? Times.Once() : Times.Never());
    }

    // --- Folder app tests ---

    [Fact]
    public void Enforce_FolderApp_DirectoryExists_DoesNotLogWarning()
    {
        var folderPath = Path.Combine(_tempDir.Path, "TestFolder");
        Directory.CreateDirectory(folderPath);

        var app = new AppEntry
        {
            Name = "FolderApp",
            IsUrlScheme = false,
            IsFolder = true,
            ExePath = folderPath,
            RestrictAcl = true,
            ManageShortcuts = false,
            AclTarget = AclTarget.Folder
        };

        _iconService.Setup(i => i.NeedsRegeneration(It.IsAny<AppEntry>())).Returns(false);
        var db = new AppDatabase { Apps = [app] };

        _service.Enforce(db);

        // Should not warn about missing target
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("FolderApp"))), Times.Never);
        // ACL should be applied
        _aclService.Verify(a => a.ApplyAcl(
            app, It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }

    [Fact]
    public void Enforce_FolderApp_DirectoryMissing_StillAppliesAcl()
    {
        // Target existence is intentionally NOT checked — the admin may be denied read access
        // on a path whose ACL is being enforced; skipping in that case would defeat the purpose.
        var app = new AppEntry
        {
            Name = "MissingFolderApp",
            IsUrlScheme = false,
            IsFolder = true,
            ExePath = @"C:\nonexistent\folder",
            RestrictAcl = true,
            ManageShortcuts = false,
            AclTarget = AclTarget.Folder
        };
        var db = new AppDatabase { Apps = [app] };

        _service.Enforce(db);

        _aclService.Verify(a => a.ApplyAcl(
            It.IsAny<AppEntry>(),
            It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }

    [Fact]
    public void Enforce_FolderApp_IconRegeneration_NoTimestampUpdate()
    {
        var folderPath = Path.Combine(_tempDir.Path, "TestFolder2");
        Directory.CreateDirectory(folderPath);

        var app = new AppEntry
        {
            Id = "fld01",
            Name = "FolderIconApp",
            IsUrlScheme = false,
            IsFolder = true,
            ExePath = folderPath,
            RestrictAcl = false,
            ManageShortcuts = false,
            AclTarget = AclTarget.Folder
        };

        _iconService.Setup(i => i.NeedsRegeneration(app)).Returns(true);
        _iconService.Setup(i => i.CreateBadgedIcon(app, null)).Returns("icon.ico");
        var db = new AppDatabase { Apps = [app] };

        var result = _service.Enforce(db);

        _iconService.Verify(i => i.CreateBadgedIcon(app, null), Times.Once);
        // Folder apps should NOT have timestamp updates (no exe to track)
        Assert.Empty(result.TimestampUpdates);
        Assert.Empty(result.TraverseGrants);
    }

    // --- AppContainer enforcement tests ---

    [Fact]
    public void Enforce_AppContainerApp_CallsEnsureProfile()
    {
        var containerService = new Mock<IAppContainerService>();
        var service = new StartupEnforcementService(
            _aclService.Object, _shortcutService.Object,
            new Mock<IBesideTargetShortcutService>().Object,
            _shortcutDiscovery.Object,
            _iconService.Object, _log.Object,
            DefaultDisplayNameResolver, containerService.Object);

        var entry = new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser" };
        var app = new AppEntry
        {
            Name = "BrowserApp",
            ExePath = _fakeExePath,
            AccountSid = "",
            AppContainerName = "ram_browser",
            RestrictAcl = false,
            ManageShortcuts = false
        };

        _iconService.Setup(i => i.NeedsRegeneration(It.IsAny<AppEntry>())).Returns(false);
        var db = new AppDatabase { Apps = [app], AppContainers = [entry] };

        service.Enforce(db);

        containerService.Verify(s => s.EnsureProfile(entry), Times.Once);
    }

    [Fact]
    public void Enforce_AppContainerApp_EnsureProfileFailure_ContinuesWithOtherApps()
    {
        var containerService = new Mock<IAppContainerService>();
        var service = new StartupEnforcementService(
            _aclService.Object, _shortcutService.Object,
            new Mock<IBesideTargetShortcutService>().Object,
            _shortcutDiscovery.Object,
            _iconService.Object, _log.Object,
            DefaultDisplayNameResolver, containerService.Object);

        var fakeExe2 = Path.Combine(_tempDir.Path, "app2.exe");
        File.WriteAllBytes(fakeExe2, []);

        var containerEntry = new AppContainerEntry { Name = "ram_fail", Sid = "S-1-15-2-1" };
        var containerApp = new AppEntry
        {
            Name = "FailContainer",
            ExePath = _fakeExePath,
            AccountSid = "",
            AppContainerName = "ram_fail",
            RestrictAcl = true,
            ManageShortcuts = false
        };
        var normalApp = new AppEntry
        {
            Name = "NormalApp",
            ExePath = fakeExe2,
            RestrictAcl = true,
            ManageShortcuts = false
        };

        containerService
            .Setup(s => s.EnsureProfile(containerEntry))
            .Throws(new InvalidOperationException("Test failure"));
        containerService
            .Setup(s => s.EnsureTraverseAccess(It.IsAny<AppContainerEntry>(), It.IsAny<string>()))
            .Returns((false, new List<string>()));
        _iconService.Setup(i => i.NeedsRegeneration(It.IsAny<AppEntry>())).Returns(false);

        var db = new AppDatabase { Apps = [containerApp, normalApp], AppContainers = [containerEntry] };

        // Should not throw — EnsureProfile failure is logged and skipped
        service.Enforce(db);

        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("ram_fail"))), Times.Once);
        // NormalApp's ACL should still be applied despite the container failure
        _aclService.Verify(a => a.ApplyAcl(
            It.Is<AppEntry>(app => app.Name == "NormalApp"),
            It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
    }

    [Fact]
    public void Enforce_AppContainerApp_RestrictAcl_CallsEnsureTraverseAccess_PopulatesTraverseGrants()
    {
        var containerService = new Mock<IAppContainerService>();
        var service = new StartupEnforcementService(
            _aclService.Object, _shortcutService.Object,
            new Mock<IBesideTargetShortcutService>().Object,
            _shortcutDiscovery.Object,
            _iconService.Object, _log.Object,
            DefaultDisplayNameResolver, containerService.Object);

        var containerEntry = new AppContainerEntry { Name = "ram_browser", DisplayName = "Browser", Sid = "S-1-15-2-1" };
        var app = new AppEntry
        {
            Name = "BrowserApp",
            ExePath = _fakeExePath,
            AccountSid = "",
            AppContainerName = "ram_browser",
            RestrictAcl = true,
            ManageShortcuts = false
        };

        _iconService.Setup(i => i.NeedsRegeneration(It.IsAny<AppEntry>())).Returns(false);
        _aclService.Setup(a => a.ResolveAclTargetPath(app)).Returns(_fakeExePath);
        var fakeAppliedPaths = new List<string> { Path.GetDirectoryName(_fakeExePath)! };
        containerService.Setup(s => s.EnsureTraverseAccess(containerEntry, It.IsAny<string>()))
            .Returns((false, fakeAppliedPaths));

        var db = new AppDatabase { Apps = [app], AppContainers = [containerEntry] };

        var result = service.Enforce(db);

        containerService.Verify(s => s.EnsureTraverseAccess(
            containerEntry,
            It.IsAny<string>()), Times.Once);
        // TraverseGrants collects all container+dir+appliedPaths for re-tracking by ApplyEnforcementResult.
        Assert.Single(result.TraverseGrants);
        Assert.Equal(containerEntry, result.TraverseGrants[0].Container);
        Assert.Equal(fakeAppliedPaths, result.TraverseGrants[0].AppliedPaths);
    }

    [Fact]
    public void Enforce_AppContainerApp_ShortcutResolver_ReturnsNullForEmptyAccountSid_WhenNoInteractiveSession()
    {
        // Container apps have empty AccountSid. In CI environments with no interactive user
        // session the resolver must return null rather than producing a path with an empty SID.
        // xunit v2 dynamic skip shows as Fail in CLI; use early return for environment mismatch.
        if (SidResolutionHelper.GetInteractiveUserSid() != null)
            return; // Interactive session present — the resolver may resolve a username, not null.

        var app = new AppEntry
        {
            Name = "ContainerApp",
            ExePath = _fakeExePath,
            AccountSid = "",
            AppContainerName = "ram_browser",
            ManageShortcuts = false,
            RestrictAcl = false
        };
        _iconService.Setup(i => i.NeedsRegeneration(It.IsAny<AppEntry>())).Returns(false);

        var db = new AppDatabase { Apps = [app] };
        var resolver = CaptureShortcutResolver(db);

        var result = resolver(new AppEntry { AccountSid = "", AppContainerName = "ram_browser", ExePath = _fakeExePath });
        Assert.Null(result);
    }

    [Fact]
    public void Enforce_AppContainerApp_ShortcutResolver_ResolvesInteractiveUser_WhenSessionAvailable()
    {
        // When an interactive desktop session exists, the resolver for a container app with
        // empty AccountSid should resolve the interactive user's username and return non-empty.
        // xunit v2 dynamic skip shows as Fail in CLI; use early return for environment mismatch.
        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        if (interactiveSid == null)
            return; // No explorer.exe session — resolver returns null, covered by the other test.

        var app = new AppEntry
        {
            Name = "ContainerApp",
            ExePath = _fakeExePath,
            AccountSid = "",
            AppContainerName = "ram_browser",
            ManageShortcuts = false,
            RestrictAcl = false
        };
        _iconService.Setup(i => i.NeedsRegeneration(It.IsAny<AppEntry>())).Returns(false);

        var db = new AppDatabase { Apps = [app] };
        var resolver = CaptureShortcutResolver(db);

        var result = resolver(new AppEntry { AccountSid = "", AppContainerName = "ram_browser", ExePath = _fakeExePath });
        Assert.NotNull(result);
        Assert.NotEmpty(result!.Value.Item1);
    }

    [Fact]
    public void Enforce_CallsEnforceBesideTargetShortcuts()
    {
        var app = new AppEntry
        {
            Name = "BesideApp",
            IsUrlScheme = false,
            ExePath = _fakeExePath,
            RestrictAcl = false,
            ManageShortcuts = false
        };

        _iconService.Setup(i => i.NeedsRegeneration(It.IsAny<AppEntry>())).Returns(false);
        var db = new AppDatabase { Apps = [app] };

        _service.Enforce(db);

        _besideTargetShortcutService.Verify(s => s.EnforceBesideTargetShortcuts(
            It.IsAny<IEnumerable<AppEntry>>(),
            It.IsAny<string>(),
            It.IsAny<Func<AppEntry, (string, string)?>>()), Times.Once);
    }

    [Fact]
    public void Enforce_ShortcutResolver_UsesSidNamesFallbackWhenSidNotResolvable()
    {
        // Use a non-existent SID that can't be resolved to a local account name
        const string fakeSid = "S-1-5-21-1111111111-2222222222-3333333333-9001";
        var app = new AppEntry
        {
            Name = "TestApp",
            IsUrlScheme = false,
            ExePath = _fakeExePath,
            AccountSid = fakeSid,
            RestrictAcl = false,
            ManageShortcuts = false
        };
        _iconService.Setup(i => i.NeedsRegeneration(It.IsAny<AppEntry>())).Returns(false);

        var db = new AppDatabase
        {
            Apps = [app],
            SidNames =
            {
                [fakeSid] = "FallbackUser"
            }
        };
        var resolver = CaptureShortcutResolver(db);

        // Assert — SidNames map username used as fallback
        var result = resolver(app);
        Assert.NotNull(result);
        Assert.Equal("FallbackUser", result.Value.Item1);
    }

    [Fact]
    public void Enforce_ShortcutResolver_ReturnsNullWhenAccountSidEmpty()
    {
        var app = new AppEntry
        {
            Name = "NoAccountApp",
            IsUrlScheme = false,
            ExePath = _fakeExePath,
            AccountSid = "",
            RestrictAcl = false,
            ManageShortcuts = false
        };

        _iconService.Setup(i => i.NeedsRegeneration(It.IsAny<AppEntry>())).Returns(false);

        var resolver = CaptureShortcutResolver(new AppDatabase { Apps = [app] });

        // Assert — null returned for apps with no AccountSid
        Assert.Null(resolver(app));
    }

    [Fact]
    public void Enforce_ShortcutResolver_ReturnsNullWhenSidNotResolvable()
    {
        const string fakeSid = "S-1-5-21-1111111111-2222222222-3333333333-9002";
        var app = new AppEntry
        {
            Name = "OrphanApp",
            IsUrlScheme = false,
            ExePath = _fakeExePath,
            AccountSid = fakeSid,
            RestrictAcl = false,
            ManageShortcuts = false
        };

        _iconService.Setup(i => i.NeedsRegeneration(It.IsAny<AppEntry>())).Returns(false);

        // Act — no SidNames entry for this SID, cannot be resolved to a username
        var resolver = CaptureShortcutResolver(new AppDatabase { Apps = [app] });

        Assert.Null(resolver(app));
    }
}
