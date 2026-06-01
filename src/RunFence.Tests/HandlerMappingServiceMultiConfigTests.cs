using Moq;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="HandlerMappingService"/> multi-config merge behavior:
/// <list type="bullet">
/// <item><see cref="HandlerMappingService.GetAllHandlerMappings"/> returns all appIds per key (main + extras).</item>
/// <item><see cref="HandlerMappingService.GetEffectiveHandlerMappings"/> returns one winner per key; extra config wins on duplicate.</item>
/// </list>
/// </summary>
public class HandlerMappingServiceMultiConfigTests
{
    private const string ExtraConfigPath1 = @"C:\configs\extra1.rfn";
    private const string ExtraConfigPath2 = @"C:\configs\extra2.rfn";

    private static (HandlerMappingService service, AppConfigIndex index) MakeService()
    {
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        var index = new AppConfigIndex(ownershipProjection, new AppIdValidator());
        return (new HandlerMappingService(index), index);
    }

    private static AppDatabase MakeDb() => new();

    // ── GetAllHandlerMappings ────────────────────────────────────────────────

    [Fact]
    public void GetAllHandlerMappings_SingleMainConfigEntry_ReturnsSingleEntryList()
    {
        // Arrange
        var (service, _) = MakeService();
        var db = MakeDb();
        db.Settings.HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = new HandlerMappingEntry("app-main")
        };

        // Act
        var all = service.GetAllHandlerMappings(db);

        // Assert
        Assert.True(all.ContainsKey(".pdf"));
        Assert.Single(all[".pdf"]);
        Assert.Equal("app-main", all[".pdf"][0].AppId);
    }

    [Fact]
    public void GetAllHandlerMappings_SameKeyInMainAndExtraConfig_ReturnsBothEntries()
    {
        // Arrange: main config maps ".pdf" → app-main; extra config maps ".pdf" → app-extra.
        // GetAllHandlerMappings must return both, main first.
        var (service, index) = MakeService();
        var normalizedPath = Path.GetFullPath(ExtraConfigPath1);
        index.AddLoadedPath(normalizedPath);
        index.AssignApp("app-extra", normalizedPath);
        service.RegisterConfigMappings(ExtraConfigPath1, new Dictionary<string, HandlerMappingEntry>
        {
            [".pdf"] = new HandlerMappingEntry("app-extra")
        });

        var db = MakeDb();
        db.Settings.HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = new HandlerMappingEntry("app-main")
        };

        // Act
        var all = service.GetAllHandlerMappings(db);

        // Assert: both app-main and app-extra appear under ".pdf"
        Assert.True(all.ContainsKey(".pdf"));
        Assert.Equal(2, all[".pdf"].Count);
        Assert.Contains(all[".pdf"], e => e.AppId == "app-main");
        Assert.Contains(all[".pdf"], e => e.AppId == "app-extra");
    }

    [Fact]
    public void GetAllHandlerMappings_SameKeyInTwoExtraConfigs_ReturnsBothExtraEntries()
    {
        // Arrange: two extra configs each map ".html" → different apps.
        var (service, index) = MakeService();
        var path1 = Path.GetFullPath(ExtraConfigPath1);
        var path2 = Path.GetFullPath(ExtraConfigPath2);
        index.AddLoadedPath(path1);
        index.AddLoadedPath(path2);
        index.AssignApp("app-extra1", path1);
        index.AssignApp("app-extra2", path2);

        service.RegisterConfigMappings(ExtraConfigPath1, new Dictionary<string, HandlerMappingEntry>
        {
            [".html"] = new HandlerMappingEntry("app-extra1")
        });
        service.RegisterConfigMappings(ExtraConfigPath2, new Dictionary<string, HandlerMappingEntry>
        {
            [".html"] = new HandlerMappingEntry("app-extra2")
        });

        var db = MakeDb();

        // Act
        var all = service.GetAllHandlerMappings(db);

        // Assert: both extra entries appear
        Assert.True(all.ContainsKey(".html"));
        Assert.Equal(2, all[".html"].Count);
        Assert.Contains(all[".html"], e => e.AppId == "app-extra1");
        Assert.Contains(all[".html"], e => e.AppId == "app-extra2");
    }

    [Fact]
    public void GetAllHandlerMappings_DifferentKeysInMainAndExtra_EachKeyHasOneEntry()
    {
        // Arrange: main maps ".pdf" → app-main; extra maps ".html" → app-extra.
        var (service, index) = MakeService();
        var normalizedPath = Path.GetFullPath(ExtraConfigPath1);
        index.AddLoadedPath(normalizedPath);
        index.AssignApp("app-extra", normalizedPath);
        service.RegisterConfigMappings(ExtraConfigPath1, new Dictionary<string, HandlerMappingEntry>
        {
            [".html"] = new HandlerMappingEntry("app-extra")
        });

        var db = MakeDb();
        db.Settings.HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = new HandlerMappingEntry("app-main")
        };

        // Act
        var all = service.GetAllHandlerMappings(db);

        // Assert
        Assert.Equal(2, all.Count);
        Assert.Single(all[".pdf"]);
        Assert.Equal("app-main", all[".pdf"][0].AppId);
        Assert.Single(all[".html"]);
        Assert.Equal("app-extra", all[".html"][0].AppId);
    }

    // ── GetEffectiveHandlerMappings ──────────────────────────────────────────

    [Fact]
    public void GetEffectiveHandlerMappings_NoExtraConfigs_ReturnsMainConfigEntries()
    {
        // Arrange
        var (service, _) = MakeService();
        var db = MakeDb();
        db.Settings.HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = new HandlerMappingEntry("app-main")
        };

        // Act
        var effective = service.GetEffectiveHandlerMappings(db);

        // Assert
        Assert.Single(effective);
        Assert.Equal("app-main", effective[".pdf"].AppId);
    }

    [Fact]
    public void GetEffectiveHandlerMappings_ExtraConfigWinsOnDuplicateKey()
    {
        // Arrange: both main and extra map ".pdf" → different apps.
        // GetEffectiveHandlerMappings must return only one winner: the extra config's entry.
        var (service, index) = MakeService();
        var normalizedPath = Path.GetFullPath(ExtraConfigPath1);
        index.AddLoadedPath(normalizedPath);
        index.AssignApp("app-extra", normalizedPath);
        service.RegisterConfigMappings(ExtraConfigPath1, new Dictionary<string, HandlerMappingEntry>
        {
            [".pdf"] = new HandlerMappingEntry("app-extra")
        });

        var db = MakeDb();
        db.Settings.HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = new HandlerMappingEntry("app-main")
        };

        // Act
        var effective = service.GetEffectiveHandlerMappings(db);

        // Assert: extra config wins
        Assert.Single(effective);
        Assert.Equal("app-extra", effective[".pdf"].AppId);
    }

    [Fact]
    public void GetEffectiveHandlerMappings_LastExtraConfigWins_WhenTwoExtrasShareKey()
    {
        // Arrange: two extra configs both map ".html". The second loaded config wins.
        var (service, index) = MakeService();
        var path1 = Path.GetFullPath(ExtraConfigPath1);
        var path2 = Path.GetFullPath(ExtraConfigPath2);
        index.AddLoadedPath(path1);
        index.AddLoadedPath(path2);
        index.AssignApp("app-extra1", path1);
        index.AssignApp("app-extra2", path2);

        service.RegisterConfigMappings(ExtraConfigPath1, new Dictionary<string, HandlerMappingEntry>
        {
            [".html"] = new HandlerMappingEntry("app-extra1")
        });
        service.RegisterConfigMappings(ExtraConfigPath2, new Dictionary<string, HandlerMappingEntry>
        {
            [".html"] = new HandlerMappingEntry("app-extra2")
        });

        var db = MakeDb();

        // Act
        var effective = service.GetEffectiveHandlerMappings(db);

        // Assert: last extra config (path2, app-extra2) wins
        Assert.Single(effective);
        Assert.Equal("app-extra2", effective[".html"].AppId);
    }

    [Fact]
    public void GetEffectiveHandlerMappings_ExtraConfigAddsNewKey_MainAndExtraKeysBothPresent()
    {
        // Arrange: main has ".pdf", extra has ".html".
        var (service, index) = MakeService();
        var normalizedPath = Path.GetFullPath(ExtraConfigPath1);
        index.AddLoadedPath(normalizedPath);
        index.AssignApp("app-extra", normalizedPath);
        service.RegisterConfigMappings(ExtraConfigPath1, new Dictionary<string, HandlerMappingEntry>
        {
            [".html"] = new HandlerMappingEntry("app-extra")
        });

        var db = MakeDb();
        db.Settings.HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = new HandlerMappingEntry("app-main")
        };

        // Act
        var effective = service.GetEffectiveHandlerMappings(db);

        // Assert
        Assert.Equal(2, effective.Count);
        Assert.Equal("app-main", effective[".pdf"].AppId);
        Assert.Equal("app-extra", effective[".html"].AppId);
    }

    [Fact]
    public void GetEffectiveHandlerMappings_UnregisteredConfig_IsExcluded()
    {
        // Arrange: register then unregister an extra config.
        var (service, index) = MakeService();
        var normalizedPath = Path.GetFullPath(ExtraConfigPath1);
        index.AddLoadedPath(normalizedPath);
        service.RegisterConfigMappings(ExtraConfigPath1, new Dictionary<string, HandlerMappingEntry>
        {
            [".pdf"] = new HandlerMappingEntry("app-extra")
        });
        service.UnregisterConfigMappings(ExtraConfigPath1);
        index.RemoveLoadedPath(normalizedPath);

        var db = MakeDb();
        db.Settings.HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = new HandlerMappingEntry("app-main")
        };

        // Act
        var effective = service.GetEffectiveHandlerMappings(db);

        // Assert: only main config entry remains
        Assert.Single(effective);
        Assert.Equal("app-main", effective[".pdf"].AppId);
    }

    [Fact]
    public void RenameAppIdInConfigMappings_NormalizesConfigPathAndPreservesOtherFields()
    {
        var (service, index) = MakeService();
        var normalizedPath = Path.GetFullPath(ExtraConfigPath1);
        index.AddLoadedPath(normalizedPath);
        index.AssignApp("old-app", normalizedPath);

        var prefixes = new List<string> { @"C:\Allowed", @"D:\More" };
        service.RegisterConfigMappings(ExtraConfigPath1, new Dictionary<string, HandlerMappingEntry>
        {
            [".txt"] = new("old-app", "\"%1\"", prefixes, true),
            ["http"] = new("other-app", null, ["https://"], false)
        });

        service.RenameAppIdInConfigMappings(Path.Combine(@"C:\configs", ".", "extra1.rfn"), "OLD-APP", "new-app");

        var mappings = service.GetHandlerMappingsForConfig(normalizedPath);

        Assert.NotNull(mappings);
        Assert.Equal("new-app", mappings![".txt"].AppId);
        Assert.Equal("\"%1\"", mappings[".txt"].ArgumentsTemplate);
        Assert.Same(prefixes, mappings[".txt"].PathPrefixes);
        Assert.True(mappings[".txt"].ReplacePrefixes);
        Assert.Equal("other-app", mappings["http"].AppId);
        Assert.Null(mappings["http"].ArgumentsTemplate);
        Assert.Equal(["https://"], mappings["http"].PathPrefixes);
        Assert.False(mappings["http"].ReplacePrefixes);
    }

    [Fact]
    public void RenameAppIdInConfigMappings_OnlyRenamesMappingsFromSpecifiedConfig()
    {
        var (service, index) = MakeService();
        var path1 = Path.GetFullPath(ExtraConfigPath1);
        var path2 = Path.GetFullPath(ExtraConfigPath2);
        index.AddLoadedPath(path1);
        index.AddLoadedPath(path2);
        index.AssignApp("old-app", path1);
        index.AssignApp("old-app-2", path2);

        service.RegisterConfigMappings(ExtraConfigPath1, new Dictionary<string, HandlerMappingEntry>
        {
            [".txt"] = new("old-app")
        });
        service.RegisterConfigMappings(ExtraConfigPath2, new Dictionary<string, HandlerMappingEntry>
        {
            [".txt"] = new("old-app")
        });

        service.RenameAppIdInConfigMappings(ExtraConfigPath1, "old-app", "new-app");

        var firstMappings = service.GetHandlerMappingsForConfig(path1);
        var secondMappings = service.GetHandlerMappingsForConfig(path2);

        Assert.NotNull(firstMappings);
        Assert.NotNull(secondMappings);
        Assert.Equal("new-app", firstMappings![".txt"].AppId);
        Assert.Equal("old-app", secondMappings![".txt"].AppId);
    }
}
