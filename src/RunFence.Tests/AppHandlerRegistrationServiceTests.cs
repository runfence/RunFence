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
    private readonly string _testSid;
    private readonly RegistryKey _hkuRoot;
    private readonly TempDirectory _tempDir;
    private readonly string _launcherPath;

    public AppHandlerRegistrationServiceTests()
    {
        _log = new Mock<ILoggingService>();
        _licenseService = new Mock<ILicenseService>();
        _licenseService.Setup(l => l.IsLicensed).Returns(true);
        _testSid = $"RunFenceTest_{Guid.NewGuid():N}";
        _hkuRoot = Registry.CurrentUser;
        _tempDir = new TempDirectory("RunFenceHandlerTests");
        _launcherPath = Path.Combine(_tempDir.Path, "RunFence.Launcher.exe");
        File.WriteAllText(_launcherPath, "stub");
    }

    public void Dispose()
    {
        try
        {
            _hkuRoot.DeleteSubKeyTree(_testSid, throwOnMissingSubKey: false);
        }
        catch
        {
        }

        _tempDir.Dispose();
    }

    private AppHandlerRegistrationService CreateService(ILicenseService? licenseService = null)
        => new(_log.Object, licenseService ?? _licenseService.Object, _hkuRoot, _testSid, _launcherPath);

    private static AppEntry MakeApp(string id, string? exePath = null)
        => new() { Id = id, Name = id, ExePath = exePath ?? "" };

    private string? ReadValue(string subKey, string? valueName = null)
    {
        using var key = _hkuRoot.OpenSubKey($@"{_testSid}\{subKey}");
        return key?.GetValue(valueName) as string;
    }

    private RegistryKey? OpenKey(string subKey)
        => _hkuRoot.OpenSubKey($@"{_testSid}\{subKey}");

    private List<string> GetProgIds()
    {
        using var classesKey = _hkuRoot.OpenSubKey($@"{_testSid}\Software\Classes");
        return classesKey?.GetSubKeyNames()
            .Where(n => n.StartsWith(Constants.HandlerProgIdPrefix, StringComparison.OrdinalIgnoreCase)
                        && !n.Equals(Constants.HandlerParentKey, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];
    }

    // --- Per-association ProgId creation ---

    [Fact]
    public void Sync_MixedExtensionsAndProtocols_CreatesCorrectPerAssociationProgIds()
    {
        var app = MakeApp("app1");
        var mappings = new Dictionary<string, string> { ["http"] = "app1", [".pdf"] = "app1" };

        CreateService().Sync(mappings, [app]);

        Assert.NotNull(OpenKey(@"Software\Classes\RunFence_http"));
        Assert.NotNull(OpenKey(@"Software\Classes\RunFence_.pdf"));
    }

    [Fact]
    public void Sync_PerAssociationCommand_UsesResolveFormat()
    {
        var app = MakeApp("app1");

        CreateService().Sync(new Dictionary<string, string> { ["http"] = "app1" }, [app]);

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

        svc.Sync(new Dictionary<string, string> { ["http"] = "app1" }, [app]);
        svc.Sync(new Dictionary<string, string> { ["http"] = "app1", ["https"] = "app1" }, [app]);

        Assert.NotNull(OpenKey(@"Software\Classes\RunFence_http"));
        Assert.NotNull(OpenKey(@"Software\Classes\RunFence_https"));
    }

    [Fact]
    public void Sync_RemovingAssociation_DeletesItsProgIdAndUpdatesCapabilities()
    {
        var app = MakeApp("app1");
        var svc = CreateService();

        svc.Sync(new Dictionary<string, string> { ["http"] = "app1", ["https"] = "app1" }, [app]);
        svc.Sync(new Dictionary<string, string> { ["http"] = "app1" }, [app]);

        Assert.NotNull(OpenKey(@"Software\Classes\RunFence_http"));
        Assert.Null(OpenKey(@"Software\Classes\RunFence_https"));

        using var urlKey = OpenKey(@"Software\Classes\RunFence_Handler\Capabilities\URLAssociations");
        Assert.NotNull(urlKey?.GetValue("http"));
        Assert.Null(urlKey.GetValue("https"));
    }

    [Fact]
    public void Sync_MixedInput_SeparatesFileAndUrlAssociationsUnderCapabilities()
    {
        var app = MakeApp("app1");

        CreateService().Sync(new Dictionary<string, string> { ["http"] = "app1", [".pdf"] = "app1" }, [app]);

        using var urlKey = OpenKey(@"Software\Classes\RunFence_Handler\Capabilities\URLAssociations");
        using var fileKey = OpenKey(@"Software\Classes\RunFence_Handler\Capabilities\FileAssociations");

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
        var mappings = new Dictionary<string, string> { ["http"] = "app1", [".pdf"] = "app1" };
        var svc = CreateService();

        svc.Sync(mappings, [app]);
        svc.Sync(mappings, [app]);

        // Exactly the expected ProgIds, no extras
        var progIds = GetProgIds();
        Assert.Contains("RunFence_http", progIds);
        Assert.Contains("RunFence_.pdf", progIds);
        Assert.Equal(2, progIds.Count);

        // URLAssociations has exactly one value
        using var urlKey = OpenKey(@"Software\Classes\RunFence_Handler\Capabilities\URLAssociations");
        var urlValues = urlKey?.GetValueNames() ?? [];
        Assert.Single(urlValues);
        Assert.Equal("http", urlValues[0]);
    }

    [Fact]
    public void Sync_EmptyMappings_CleansAllProgIds()
    {
        var app = MakeApp("app1");
        var svc = CreateService();

        svc.Sync(new Dictionary<string, string> { ["http"] = "app1" }, [app]);
        svc.Sync(new Dictionary<string, string>(), [app]);

        Assert.Null(OpenKey(@"Software\Classes\RunFence_http"));
    }

    [Fact]
    public void Sync_SkipsMissingAppEntries_LogsWarning()
    {
        var mappings = new Dictionary<string, string> { ["http"] = "nonexistent_app" };

        CreateService().Sync(mappings, []);

        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("app not found"))), Times.Once);
        Assert.Null(OpenKey(@"Software\Classes\RunFence_http"));
    }

    [Fact]
    public void Sync_RejectsInvalidKeys()
    {
        var app = MakeApp("app1");
        // Keys with backslash, space, percent — all invalid
        var mappings = new Dictionary<string, string>
        {
            [@"http\evil"] = "app1",
            ["has space"] = "app1",
            ["has%percent"] = "app1"
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

        CreateService().Sync(new Dictionary<string, string> { ["http"] = "app1", [".pdf"] = "app2" }, [app1, app2]);

        var icon1 = ReadValue(@"Software\Classes\RunFence_http\DefaultIcon");
        var icon2 = ReadValue(@"Software\Classes\RunFence_.pdf\DefaultIcon");
        Assert.NotNull(icon1);
        Assert.NotNull(icon2);
        Assert.Contains(_launcherPath, icon1);
        Assert.Contains(_launcherPath, icon2);
    }

    // --- UnregisterAll ---

    [Fact]
    public void UnregisterAll_CleansAllProgIdsAndCapabilitiesAndRegisteredApps()
    {
        var app = MakeApp("app1");
        var svc = CreateService();

        svc.Sync(new Dictionary<string, string> { ["http"] = "app1", [".pdf"] = "app1" }, [app]);
        svc.UnregisterAll();

        Assert.Null(OpenKey(@"Software\Classes\RunFence_http"));
        Assert.Null(OpenKey(@"Software\Classes\RunFence_.pdf"));
        Assert.Null(OpenKey($@"Software\Classes\{Constants.HandlerParentKey}"));

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
        var mappings = new Dictionary<string, string>
        {
            ["http"] = "app1", ["https"] = "app1", [".htm"] = "app1", [".html"] = "app1",
            [".pdf"] = "app1", ["mailto"] = "app1"
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
        var mappings = new Dictionary<string, string>
        {
            ["http"] = "app1", ["https"] = "app1", [".htm"] = "app1", [".html"] = "app1"
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
        var effectiveMappings = new Dictionary<string, string> { ["http"] = "extraApp" };

        CreateService().Sync(effectiveMappings, [mainApp, extraApp]);

        Assert.NotNull(OpenKey(@"Software\Classes\RunFence_http"));
        var cmd = ReadValue(@"Software\Classes\RunFence_http\shell\open\command");
        Assert.NotNull(cmd);
        Assert.Contains("\"http\"", cmd);
        // Exactly one ProgId for http (not two)
        Assert.Single(GetProgIds());
    }
}