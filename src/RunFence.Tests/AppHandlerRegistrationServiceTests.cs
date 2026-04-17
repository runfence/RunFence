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
        _hklmRoot = Registry.CurrentUser.CreateSubKey(_testSubKey)!;
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
            .Where(n => n.StartsWith(Constants.HandlerProgIdPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];
    }

    // --- Per-association ProgId creation ---

    [Fact]
    public void Sync_MixedExtensionsAndProtocols_CreatesCorrectPerAssociationProgIds()
    {
        var app = MakeApp("app1");
        var mappings = new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1"), [".pdf"] = new HandlerMappingEntry("app1") };

        CreateService().Sync(mappings, [app]);

        Assert.NotNull(OpenKey(@"Software\Classes\RunFence_http"));
        Assert.NotNull(OpenKey(@"Software\Classes\RunFence_.pdf"));
    }

    [Fact]
    public void Sync_PerAssociationCommand_UsesResolveFormat()
    {
        var app = MakeApp("app1");

        CreateService().Sync(new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1") }, [app]);

        var cmd = ReadValue(@"Software\Classes\RunFence_http\shell\open\command");
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

        Assert.NotNull(OpenKey(@"Software\Classes\RunFence_http"));
        Assert.NotNull(OpenKey(@"Software\Classes\RunFence_https"));
    }

    [Fact]
    public void Sync_RegisteredApplicationsPointsToCapabilitiesOutsideClasses()
    {
        var app = MakeApp("app1");

        CreateService().Sync(new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1") }, [app]);

        // RegisteredApplications must point to the standard location outside Software\Classes
        // to prevent Windows from double-discovering RunFence in Default Apps.
        var regAppsValue = ReadValue(@"Software\RegisteredApplications", Constants.HandlerRegisteredAppName);
        Assert.Equal(Constants.HandlerCapabilitiesRegistryPath, regAppsValue);
        Assert.DoesNotContain(@"Software\Classes", regAppsValue!);
    }

    [Fact]
    public void Sync_RemovingAssociation_DeletesItsProgIdAndUpdatesCapabilities()
    {
        var app = MakeApp("app1");
        var svc = CreateService();

        svc.Sync(new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1"), ["https"] = new HandlerMappingEntry("app1") }, [app]);
        svc.Sync(new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1") }, [app]);

        Assert.NotNull(OpenKey(@"Software\Classes\RunFence_http"));
        Assert.Null(OpenKey(@"Software\Classes\RunFence_https"));

        using var urlKey = OpenKey(@"Software\RunFence\Capabilities\URLAssociations");
        Assert.NotNull(urlKey?.GetValue("http"));
        Assert.Null(urlKey.GetValue("https"));
    }

    [Fact]
    public void Sync_MixedInput_SeparatesFileAndUrlAssociationsUnderCapabilities()
    {
        var app = MakeApp("app1");

        CreateService().Sync(new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1"), [".pdf"] = new HandlerMappingEntry("app1") }, [app]);

        using var urlKey = OpenKey(@"Software\RunFence\Capabilities\URLAssociations");
        using var fileKey = OpenKey(@"Software\RunFence\Capabilities\FileAssociations");

        Assert.Equal("RunFence_http", urlKey?.GetValue("http") as string);
        Assert.Equal("RunFence_.pdf", fileKey?.GetValue(".pdf") as string);
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
        Assert.Contains("RunFence_http", progIds);
        Assert.Contains("RunFence_.pdf", progIds);
        Assert.Equal(2, progIds.Count);

        // URLAssociations has exactly one value
        using var urlKey = OpenKey(@"Software\RunFence\Capabilities\URLAssociations");
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

        Assert.Null(OpenKey(@"Software\Classes\RunFence_http"));
    }

    [Fact]
    public void Sync_SkipsMissingAppEntries_LogsWarning()
    {
        var mappings = new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("nonexistent_app") };

        CreateService().Sync(mappings, []);

        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("app not found"))), Times.Once);
        Assert.Null(OpenKey(@"Software\Classes\RunFence_http"));
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

        var icon1 = ReadValue(@"Software\Classes\RunFence_http\DefaultIcon");
        var icon2 = ReadValue(@"Software\Classes\RunFence_.pdf\DefaultIcon");
        Assert.NotNull(icon1);
        Assert.NotNull(icon2);
        Assert.Contains(_launcherPath, icon1);
        Assert.Contains(_launcherPath, icon2);
    }

    [Fact]
    public void Sync_LegacyRunFenceHandlerKeyInClasses_IsRemovedToPreventDuplicate()
    {
        // Pre-create legacy RunFence_Handler key as it existed in older installations
        // (Capabilities were inside Software\Classes\RunFence_Handler instead of Software\RunFence\Capabilities)
        _hklmRoot.CreateSubKey(@"Software\Classes\RunFence_Handler\Capabilities")!.Dispose();

        var app = MakeApp("app1");
        CreateService().Sync(new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1") }, [app]);

        // Legacy key must be removed — its presence inside Software\Classes caused Windows
        // to show RunFence twice in Default Apps.
        Assert.Null(OpenKey(@"Software\Classes\RunFence_Handler"));
        // New Capabilities must be at the standard location
        Assert.NotNull(OpenKey(@"Software\RunFence\Capabilities"));
    }

    // --- UnregisterAll ---

    [Fact]
    public void UnregisterAll_CleansAllProgIdsAndCapabilitiesAndRegisteredApps()
    {
        var app = MakeApp("app1");
        var svc = CreateService();

        svc.Sync(new Dictionary<string, HandlerMappingEntry> { ["http"] = new HandlerMappingEntry("app1"), [".pdf"] = new HandlerMappingEntry("app1") }, [app]);
        svc.UnregisterAll();

        Assert.Null(OpenKey(@"Software\Classes\RunFence_http"));
        Assert.Null(OpenKey(@"Software\Classes\RunFence_.pdf"));
        Assert.Null(OpenKey(@"Software\RunFence\Capabilities"));

        using var regApps = OpenKey(@"Software\RegisteredApplications");
        Assert.Null(regApps?.GetValue(Constants.HandlerRegisteredAppName));
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

        Assert.NotNull(OpenKey(@"Software\Classes\RunFence_http"));
        Assert.NotNull(OpenKey(@"Software\Classes\RunFence_https"));
        Assert.NotNull(OpenKey(@"Software\Classes\RunFence_.htm"));
        Assert.NotNull(OpenKey(@"Software\Classes\RunFence_.html"));
        Assert.Null(OpenKey(@"Software\Classes\RunFence_.pdf"));
        Assert.Null(OpenKey(@"Software\Classes\RunFence_mailto"));
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
        Assert.Contains("RunFence_http", progIds);
        Assert.Contains("RunFence_https", progIds);
        Assert.Contains("RunFence_.htm", progIds);
        Assert.Contains("RunFence_.html", progIds);
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

        Assert.NotNull(OpenKey(@"Software\Classes\RunFence_http"));
        var cmd = ReadValue(@"Software\Classes\RunFence_http\shell\open\command");
        Assert.NotNull(cmd);
        Assert.Contains("\"http\"", cmd);
        // Exactly one ProgId for http (not two)
        Assert.Single(GetProgIds());
    }
}
