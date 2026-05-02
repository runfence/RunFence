using Moq;
using RunFence.Core.Models;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class HandlerMappingServiceDirectHandlerTests
{
    private static AppDatabase MakeDb() => new();

    private static (HandlerMappingService service, AppConfigIndex index) MakeService()
    {
        var index = new AppConfigIndex(new Mock<IGrantConfigTracker>().Object);
        return (new HandlerMappingService(index), index);
    }

    [Fact]
    public void GetEffectiveDirectHandlerMappings_WhenNull_ReturnsEmpty()
    {
        var (service, _) = MakeService();
        var db = MakeDb();

        var result = service.GetEffectiveDirectHandlerMappings(db);

        Assert.Empty(result);
    }

    [Fact]
    public void GetEffectiveDirectHandlerMappings_ReturnsMainConfigEntries()
    {
        var (service, _) = MakeService();
        var db = MakeDb();
        db.Settings.DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = new DirectHandlerEntry { ClassName = "txtfile" }
        };

        var result = service.GetEffectiveDirectHandlerMappings(db);

        Assert.Single(result);
        Assert.Equal("txtfile", result[".txt"].ClassName);
    }

    [Fact]
    public void SetDirectHandlerMapping_AddsToMainConfig()
    {
        var (service, _) = MakeService();
        var db = MakeDb();
        var entry = new DirectHandlerEntry { ClassName = "txtfile" };

        service.SetDirectHandlerMapping(".txt", entry, db);

        Assert.NotNull(db.Settings.DirectHandlerMappings);
        Assert.Equal("txtfile", db.Settings.DirectHandlerMappings[".txt"].ClassName);
    }

    [Fact]
    public void SetDirectHandlerMapping_RemovesConflictingMainConfigAppMapping()
    {
        var (service, _) = MakeService();
        var db = MakeDb();
        db.Settings.HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = new HandlerMappingEntry("app1")
        };
        var entry = new DirectHandlerEntry { ClassName = "txtfile" };

        service.SetDirectHandlerMapping(".txt", entry, db);

        // App mapping removed
        Assert.Null(db.Settings.HandlerMappings);
        // Direct handler added
        Assert.Equal("txtfile", db.Settings.DirectHandlerMappings![".txt"].ClassName);
    }

    [Fact]
    public void SetDirectHandlerMapping_PreservesExtraConfigAppMappings()
    {
        var (service, index) = MakeService();
        index.AddLoadedPath(Path.GetFullPath(@"C:\extra.rfn"));
        index.AssignApp("appExtra", Path.GetFullPath(@"C:\extra.rfn"));
        service.RegisterConfigMappings(@"C:\extra.rfn",
            new Dictionary<string, HandlerMappingEntry> { [".txt"] = new HandlerMappingEntry("appExtra") });

        var db = MakeDb();
        var entry = new DirectHandlerEntry { ClassName = "txtfile" };

        service.SetDirectHandlerMapping(".txt", entry, db);

        // Extra config mapping preserved
        var all = service.GetAllHandlerMappings(db);
        Assert.True(all.ContainsKey(".txt"));
        Assert.Contains(all[".txt"], e => e.AppId == "appExtra");
    }

    [Fact]
    public void RemoveDirectHandlerMapping_RemovesEntry()
    {
        var (service, _) = MakeService();
        var db = MakeDb();
        db.Settings.DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = new DirectHandlerEntry { ClassName = "txtfile" }
        };

        service.RemoveDirectHandlerMapping(".txt", db);

        Assert.Null(db.Settings.DirectHandlerMappings);
    }

    [Fact]
    public void RemoveDirectHandlerMapping_NullsWhenEmpty()
    {
        var (service, _) = MakeService();
        var db = MakeDb();
        db.Settings.DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = new DirectHandlerEntry { ClassName = "txtfile" },
            [".py"] = new DirectHandlerEntry { Command = "python.exe" }
        };

        service.RemoveDirectHandlerMapping(".txt", db);

        Assert.NotNull(db.Settings.DirectHandlerMappings);
        Assert.Single(db.Settings.DirectHandlerMappings);

        service.RemoveDirectHandlerMapping(".py", db);

        Assert.Null(db.Settings.DirectHandlerMappings);
    }

    [Fact]
    public void RemoveDirectHandlerMapping_NoOp_WhenNull()
    {
        var (service, _) = MakeService();
        var db = MakeDb();

        // Must not throw
        service.RemoveDirectHandlerMapping(".txt", db);

        Assert.Null(db.Settings.DirectHandlerMappings);
    }

    // ── TC-26: extra-config direct handler negative test ─────────────────────

    [Fact]
    public void GetEffectiveDirectHandlerMappings_ExtraConfigHasNoDirectHandlers_ReturnsMainConfigOnly()
    {
        // Arrange — extra config is loaded with app-based mappings only, no direct handler mappings.
        // Direct handler mappings are only supported in main config.
        var (service, index) = MakeService();
        index.AddLoadedPath(Path.GetFullPath(@"C:\extra.rfn"));
        index.AssignApp("appExtra", Path.GetFullPath(@"C:\extra.rfn"));

        // Register app-based handler mappings for the extra config (NOT direct handler mappings)
        service.RegisterConfigMappings(@"C:\extra.rfn",
            new Dictionary<string, HandlerMappingEntry> { [".pdf"] = new HandlerMappingEntry("appExtra") });

        var db = MakeDb();
        db.Settings.DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = new DirectHandlerEntry { ClassName = "txtfile" }
        };

        // Act — GetEffectiveDirectHandlerMappings only returns main-config direct handlers
        var result = service.GetEffectiveDirectHandlerMappings(db);

        // Assert — only main-config direct handler returned; extra-config app mapping not exposed as direct handler
        Assert.Single(result);
        Assert.True(result.ContainsKey(".txt"));
        Assert.False(result.ContainsKey(".pdf"));
    }

    [Fact]
    public void SetDirectHandlerMapping_OnlyRemovesConflictingMainConfigMapping_NotExtraConfig()
    {
        // Arrange — extra config has .txt handler, main config also has .txt handler.
        // SetDirectHandlerMapping removes only the main-config conflict.
        var (service, index) = MakeService();
        index.AddLoadedPath(Path.GetFullPath(@"C:\extra.rfn"));
        index.AssignApp("appExtra", Path.GetFullPath(@"C:\extra.rfn"));
        service.RegisterConfigMappings(@"C:\extra.rfn",
            new Dictionary<string, HandlerMappingEntry> { [".txt"] = new HandlerMappingEntry("appExtra") });

        var db = MakeDb();
        db.Settings.HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = new HandlerMappingEntry("appMain")
        };

        var entry = new DirectHandlerEntry { ClassName = "txtfile" };

        // Act
        service.SetDirectHandlerMapping(".txt", entry, db);

        // Assert — main-config .txt mapping removed, direct handler added
        Assert.Null(db.Settings.HandlerMappings);
        Assert.NotNull(db.Settings.DirectHandlerMappings);
        Assert.Equal("txtfile", db.Settings.DirectHandlerMappings![".txt"].ClassName);

        // Extra-config mapping for .txt is preserved (not removed by SetDirectHandlerMapping)
        var all = service.GetAllHandlerMappings(db);
        Assert.True(all.ContainsKey(".txt"));
        Assert.Contains(all[".txt"], e => e.AppId == "appExtra");
    }

    [Fact]
    public void GetEffectiveDirectHandlerMappings_ExtraConfigCannotOverrideDirectHandlers()
    {
        // Direct handler mappings are main-config-only.
        // Even if an extra config is loaded, it cannot introduce or override direct handler entries.
        var (service, index) = MakeService();
        index.AddLoadedPath(Path.GetFullPath(@"C:\extra.rfn"));
        service.RegisterConfigMappings(@"C:\extra.rfn",
            new Dictionary<string, HandlerMappingEntry> { [".htm"] = new HandlerMappingEntry("appExtra") });

        var db = MakeDb();
        db.Settings.DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".htm"] = new DirectHandlerEntry { Command = "custom-browser.exe" }
        };

        // Act — the direct handler for .htm from main config is returned as-is
        var result = service.GetEffectiveDirectHandlerMappings(db);

        // Assert — only the main-config direct handler survives
        Assert.Single(result);
        Assert.Equal("custom-browser.exe", result[".htm"].Command);
    }
}
