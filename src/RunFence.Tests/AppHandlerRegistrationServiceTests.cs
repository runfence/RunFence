using Microsoft.Win32;
using Moq;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Licensing;
using Xunit;

namespace RunFence.Tests;

public class AppHandlerRegistrationServiceTests : IDisposable
{
    private readonly Mock<ILoggingService> _log;
    private readonly Mock<ILicenseService> _licenseService;
    private readonly string _testSubKey;
    private readonly RegistryKey _hklmRoot;
    private readonly TempDirectory _tempDir;
    private readonly string _launcherPath;

    public AppHandlerRegistrationServiceTests()
    {
        _log = new Mock<ILoggingService>();
        _licenseService = new Mock<ILicenseService>();
        _licenseService.Setup(l => l.IsLicensed).Returns(true);
        _testSubKey = $@"Software\RunFenceTests\HandlerReg_{Guid.NewGuid():N}";
        _hklmRoot = Registry.CurrentUser.CreateSubKey(_testSubKey);
        _tempDir = new TempDirectory("RunFenceHandlerTests");
        _launcherPath = Path.Combine(_tempDir.Path, "RunFence.Launcher.exe");
        File.WriteAllText(_launcherPath, "stub");
    }

    public void Dispose()
    {
        _hklmRoot.Dispose();
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(_testSubKey, throwOnMissingSubKey: false);
        }
        catch
        {
        }

        _tempDir.Dispose();
    }

    private AppHandlerRegistrationService CreateService(ILicenseService? licenseService = null)
        => new(_log.Object, licenseService ?? _licenseService.Object, _hklmRoot, _launcherPath);

    private static AppEntry MakeApp(string id, string? exePath = null)
        => new() { Id = id, Name = id, ExePath = exePath ?? "" };

    private string? ReadValue(string subKey, string? valueName = null)
    {
        using var key = _hklmRoot.OpenSubKey(subKey);
        return key?.GetValue(valueName) as string;
    }

    private RegistryKey? OpenKey(string subKey)
        => _hklmRoot.OpenSubKey(subKey);

    private List<string> GetProgIds()
    {
        using var classesKey = _hklmRoot.OpenSubKey(@"Software\Classes");
        return classesKey?.GetSubKeyNames()
            .Where(n => n.StartsWith(PathConstants.HandlerProgIdPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];
    }

    // Derives a relative subkey path for a ProgId under Software\Classes
    private static string ProgIdKey(string association)
        => $@"Software\Classes\{PathConstants.HandlerProgIdPrefix}{association}";

    // Derives the URLAssociations key path relative to _hklmRoot
    private static string UrlAssociationsKey()
        => PathConstants.HandlerCapabilitiesRegistryPath + @"\URLAssociations";

    // Derives the FileAssociations key path relative to _hklmRoot
    private static string FileAssociationsKey()
        => PathConstants.HandlerCapabilitiesRegistryPath + @"\FileAssociations";

    // --- Per-association ProgId creation ---

    [Fact]
    public void Sync_MixedExtensionsAndProtocols_CreatesCorrectPerAssociationProgIds()
    {
        var app = MakeApp("app1");
        var mappings = new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1"), [".pdf"] = new HandlerMappingEntry("app1") };

        CreateService().Sync(mappings, [app]);

        Assert.NotNull(OpenKey(ProgIdKey("http")));
        Assert.NotNull(OpenKey(ProgIdKey(".pdf")));
    }

    [Fact]
    public void Sync_PerAssociationCommand_UsesResolveFormat()
    {
        var app = MakeApp("app1");

        CreateService().Sync(new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1") }, [app]);

        var cmd = ReadValue(ProgIdKey("http") + @"\shell\open\command");
        Assert.NotNull(cmd);
        Assert.Contains("--resolve", cmd);
        Assert.Contains("\"http\"", cmd);
        Assert.Contains("%1", cmd);
        Assert.Contains(_launcherPath, cmd);
    }

    [Fact]
    public void Sync_AddingNewAssociation_CreatesProgIdWithoutRemovingExisting()
    {
        var app = MakeApp("app1");
        var svc = CreateService();

        svc.Sync(new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1") }, [app]);
        svc.Sync(new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1"), ["https"] = new HandlerMappingEntry("app1") }, [app]);

        Assert.NotNull(OpenKey(ProgIdKey("http")));
        Assert.NotNull(OpenKey(ProgIdKey("https")));
    }

    [Fact]
    public void Sync_RegisteredApplicationsPointsToCapabilitiesOutsideClasses()
    {
        var app = MakeApp("app1");

        CreateService().Sync(new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1") }, [app]);

        // RegisteredApplications must point to the standard location outside Software\Classes
        // to prevent Windows from double-discovering RunFence in Default Apps.
        var regAppsValue = ReadValue(@"Software\RegisteredApplications", PathConstants.HandlerRegisteredAppName);
        Assert.Equal(PathConstants.HandlerCapabilitiesRegistryPath, regAppsValue);
        Assert.DoesNotContain(@"Software\Classes", regAppsValue!);
    }

    [Fact]
    public void Sync_RemovingAssociation_DeletesItsProgIdAndUpdatesCapabilities()
    {
        var app = MakeApp("app1");
        var svc = CreateService();

        svc.Sync(new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1"), ["https"] = new HandlerMappingEntry("app1") }, [app]);
        svc.Sync(new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1") }, [app]);

        Assert.NotNull(OpenKey(ProgIdKey("http")));
        Assert.Null(OpenKey(ProgIdKey("https")));

        using var urlKey = OpenKey(UrlAssociationsKey());
        Assert.NotNull(urlKey?.GetValue("http"));
        Assert.Null(urlKey.GetValue("https"));
    }

    [Fact]
    public void Sync_MixedInput_SeparatesFileAndUrlAssociationsUnderCapabilities()
    {
        var app = MakeApp("app1");

        CreateService().Sync(new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1"), [".pdf"] = new HandlerMappingEntry("app1") }, [app]);

        using var urlKey = OpenKey(UrlAssociationsKey());
        using var fileKey = OpenKey(FileAssociationsKey());

        Assert.Equal(PathConstants.HandlerProgIdPrefix + "http", urlKey?.GetValue("http") as string);
        Assert.Equal(PathConstants.HandlerProgIdPrefix + ".pdf", fileKey?.GetValue(".pdf") as string);
        // Cross-check: extensions not in URL, protocols not in File
        Assert.Null(urlKey?.GetValue(".pdf"));
        Assert.Null(fileKey?.GetValue("http"));
    }

    [Fact]
    public void Sync_Idempotent_CalledTwiceProducesSameState()
    {
        var app = MakeApp("app1");
        var mappings = new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1"), [".pdf"] = new HandlerMappingEntry("app1") };
        var svc = CreateService();

        svc.Sync(mappings, [app]);
        svc.Sync(mappings, [app]);

        // Exactly the expected ProgIds, no extras
        var progIds = GetProgIds();
        Assert.Contains(PathConstants.HandlerProgIdPrefix + "http", progIds);
        Assert.Contains(PathConstants.HandlerProgIdPrefix + ".pdf", progIds);
        Assert.Equal(2, progIds.Count);

        // URLAssociations has exactly one value
        using var urlKey = OpenKey(UrlAssociationsKey());
        var urlValues = urlKey?.GetValueNames() ?? [];
        Assert.Single(urlValues);
        Assert.Equal("http", urlValues[0]);
    }

    [Fact]
    public void Sync_EmptyMappings_CleansAllProgIds()
    {
        var app = MakeApp("app1");
        var svc = CreateService();

        svc.Sync(new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1") }, [app]);
        svc.Sync(new Dictionary<string, HandlerMappingEntry>(), [app]);

        Assert.Null(OpenKey(ProgIdKey("http")));
    }

    [Fact]
    public void Sync_SkipsMissingAppEntries_LogsWarning()
    {
        var mappings = new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("nonexistent_app") };

        CreateService().Sync(mappings, []);

        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("app not found"))), Times.Once);
        Assert.Null(OpenKey(ProgIdKey("http")));
    }

    [Fact]
    public void Sync_RejectsInvalidKeys()
    {
        var app = MakeApp("app1");
        // Keys with backslash, space, percent — all invalid
        var mappings = new Dictionary<string, HandlerMappingEntry>
        {
            [@"http\evil"] = new HandlerMappingEntry("app1"),
            ["has space"] = new HandlerMappingEntry("app1"),
            ["has%percent"] = new HandlerMappingEntry("app1")
        };

        CreateService().Sync(mappings, [app]);

        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("unsafe characters"))), Times.Exactly(3));
        Assert.Empty(GetProgIds());
    }

    [Fact]
    public void Sync_DifferentAppsGetCorrectIcons()
    {
        // App exe paths don't exist → both fall back to launcher path as icon
        var app1 = MakeApp("app1");
        var app2 = MakeApp("app2");

        CreateService().Sync(new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1"), [".pdf"] = new HandlerMappingEntry("app2") }, [app1, app2]);

        var icon1 = ReadValue(ProgIdKey("http") + @"\DefaultIcon");
        var icon2 = ReadValue(ProgIdKey(".pdf") + @"\DefaultIcon");
        Assert.NotNull(icon1);
        Assert.NotNull(icon2);
        Assert.Contains(_launcherPath, icon1);
        Assert.Contains(_launcherPath, icon2);
    }

    [Fact]
    public void Sync_LegacyRunFenceHandlerKeyInClasses_IsRemovedToPreventDuplicate()
    {
        // Pre-create a stale ProgId-prefixed Handler key as it might exist from an older installation
        // (Capabilities were inside Software\Classes\{Prefix}Handler instead of the standard location)
        var legacyHandlerKey = ProgIdKey("Handler");
        _hklmRoot.CreateSubKey(legacyHandlerKey + @"\Capabilities").Dispose();

        var app = MakeApp("app1");
        CreateService().Sync(new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1") }, [app]);

        // Legacy handler key must be removed — its presence inside Software\Classes caused Windows
        // to show RunFence twice in Default Apps.
        Assert.Null(OpenKey(legacyHandlerKey));
        // New Capabilities must be at the standard location
        Assert.NotNull(OpenKey(PathConstants.HandlerCapabilitiesRegistryPath));
    }

    // --- UnregisterAll ---

    [Fact]
    public void UnregisterAll_CleansAllProgIdsAndCapabilitiesAndRegisteredApps()
    {
        var app = MakeApp("app1");
        var svc = CreateService();

        svc.Sync(new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1"), [".pdf"] = new HandlerMappingEntry("app1") }, [app]);
        svc.UnregisterAll();

        Assert.Null(OpenKey(ProgIdKey("http")));
        Assert.Null(OpenKey(ProgIdKey(".pdf")));
        Assert.Null(OpenKey(PathConstants.HandlerCapabilitiesRegistryPath));

        using var regApps = OpenKey(@"Software\RegisteredApplications");
        Assert.Null(regApps?.GetValue(PathConstants.HandlerRegisteredAppName));
    }

    // --- License filtering ---

    [Fact]
    public void Sync_FiltersNonBrowserAssociations_WhenUnlicensed()
    {
        var license = new Mock<ILicenseService>();
        license.Setup(l => l.IsLicensed).Returns(false);
        var app = MakeApp("app1");
        var mappings = new Dictionary<string, HandlerMappingEntry>
        {
            ["http"] = new HandlerMappingEntry("app1"), ["https"] = new HandlerMappingEntry("app1"),
            [".htm"] = new HandlerMappingEntry("app1"), [".html"] = new HandlerMappingEntry("app1"),
            [".pdf"] = new HandlerMappingEntry("app1"), ["mailto"] = new HandlerMappingEntry("app1")
        };

        CreateService(license.Object).Sync(mappings, [app]);

        Assert.NotNull(OpenKey(ProgIdKey("http")));
        Assert.NotNull(OpenKey(ProgIdKey("https")));
        Assert.NotNull(OpenKey(ProgIdKey(".htm")));
        Assert.NotNull(OpenKey(ProgIdKey(".html")));
        Assert.Null(OpenKey(ProgIdKey(".pdf")));
        Assert.Null(OpenKey(ProgIdKey("mailto")));
    }

    [Fact]
    public void Sync_AllowsBrowserAssociations_WhenUnlicensed()
    {
        var license = new Mock<ILicenseService>();
        license.Setup(l => l.IsLicensed).Returns(false);
        var app = MakeApp("app1");
        var mappings = new Dictionary<string, HandlerMappingEntry>
        {
            ["http"] = new HandlerMappingEntry("app1"), ["https"] = new HandlerMappingEntry("app1"),
            [".htm"] = new HandlerMappingEntry("app1"), [".html"] = new HandlerMappingEntry("app1")
        };

        CreateService(license.Object).Sync(mappings, [app]);

        var progIds = GetProgIds();
        Assert.Equal(4, progIds.Count);
        Assert.Contains(PathConstants.HandlerProgIdPrefix + "http", progIds);
        Assert.Contains(PathConstants.HandlerProgIdPrefix + "https", progIds);
        Assert.Contains(PathConstants.HandlerProgIdPrefix + ".htm", progIds);
        Assert.Contains(PathConstants.HandlerProgIdPrefix + ".html", progIds);
    }

    // --- Effective merged mappings ---

    [Fact]
    public void Sync_UsesEffectiveMergedMappings()
    {
        // Sync accepts the effective merged result — when extra config overrides main on duplicate key,
        // only the effective (winning) app gets the ProgId.
        var mainApp = MakeApp("mainApp");
        var extraApp = MakeApp("extraApp");

        // Effective: "http" → extraApp won (extra config overrides main)
        var effectiveMappings = new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("extraApp") };

        CreateService().Sync(effectiveMappings, [mainApp, extraApp]);

        Assert.NotNull(OpenKey(ProgIdKey("http")));
        var cmd = ReadValue(ProgIdKey("http") + @"\shell\open\command");
        Assert.NotNull(cmd);
        Assert.Contains("\"http\"", cmd);
        // Exactly one ProgId for http (not two)
        Assert.Single(GetProgIds());
    }
}
