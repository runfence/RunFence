using Moq;
using RunFence.Account.UI.AppContainer;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Container;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class AppContainerEditServiceTests
{
    private readonly Mock<IAppContainerService> _containerService = new();
    private readonly Mock<ILoggingService> _log = new();

    private AppContainerEditService CreateHandler(AppDatabase? database = null)
    {
        var db = database ?? new AppDatabase();
        return new(_containerService.Object, new LambdaDatabaseProvider(() => db), _log.Object);
    }

    // ── CreateNewContainer ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateNewContainer_NonEphemeral_AddsEntryToDatabase()
    {
        var db = new AppDatabase();
        _containerService.Setup(s => s.CreateProfile(It.IsAny<AppContainerEntry>()));
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");

        var result = await CreateHandler(db).CreateNewContainer(
            "rfn_test", "Test", isEphemeral: false, [], loopback: false, []);

        Assert.NotNull(result.Entry);
        Assert.Single(db.AppContainers);
        Assert.False(result.Entry!.IsEphemeral);
        Assert.Null(result.Entry.DeleteAfterUtc);
    }

    [Fact]
    public async Task CreateNewContainer_Ephemeral_SetsIsEphemeralAndDeleteAfterUtc()
    {
        var before = DateTime.UtcNow;
        _containerService.Setup(s => s.CreateProfile(It.IsAny<AppContainerEntry>()));
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");

        var result = await CreateHandler().CreateNewContainer(
            "rfn_eabc123", "Temp", isEphemeral: true, [], loopback: false, []);

        Assert.NotNull(result.Entry);
        Assert.True(result.Entry!.IsEphemeral);
        Assert.NotNull(result.Entry.DeleteAfterUtc);
        var after = DateTime.UtcNow;
        Assert.InRange(result.Entry.DeleteAfterUtc!.Value, before.AddHours(24).AddSeconds(-5), after.AddHours(24).AddSeconds(5));
    }

    [Fact]
    public async Task CreateNewContainer_DuplicateProfileName_ReturnsNullWithValidationError()
    {
        var db = new AppDatabase();
        db.AppContainers.Add(new AppContainerEntry { Name = "rfn_test", DisplayName = "Existing" });

        var result = await CreateHandler(db).CreateNewContainer(
            "rfn_test", "Another", isEphemeral: false, [], loopback: false, []);

        Assert.Null(result.Entry);
        Assert.NotNull(result.ValidationError);
        _containerService.Verify(s => s.CreateProfile(It.IsAny<AppContainerEntry>()), Times.Never);
    }

    [Fact]
    public async Task CreateNewContainer_ProfileCreationThrows_ReturnsNullWithCreationError_EntryNotAdded()
    {
        var db = new AppDatabase();
        _containerService.Setup(s => s.CreateProfile(It.IsAny<AppContainerEntry>()))
            .Throws(new InvalidOperationException("OS error"));

        var result = await CreateHandler(db).CreateNewContainer(
            "rfn_test", "Test", isEphemeral: false, [], loopback: false, []);

        Assert.Null(result.Entry);
        Assert.NotNull(result.CreationError);
        Assert.Empty(db.AppContainers);
    }

    [Theory]
    [InlineData(true, true)]   // loopback OS call succeeds → EnableLoopback = true
    [InlineData(false, false)] // loopback OS call fails → EnableLoopback = false
    public async Task CreateNewContainer_LoopbackRequested_EnableLoopbackReflectsOsResult(
        bool osCallSucceeds, bool expectedEnableLoopback)
    {
        _containerService.Setup(s => s.CreateProfile(It.IsAny<AppContainerEntry>()));
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        _containerService.Setup(s => s.SetLoopbackExemption(It.IsAny<string>(), true))
            .ReturnsAsync(osCallSucceeds);

        var result = await CreateHandler().CreateNewContainer(
            "rfn_test", "Test", isEphemeral: false, [], loopback: true, []);

        Assert.NotNull(result.Entry);
        Assert.Equal(expectedEnableLoopback, result.Entry!.EnableLoopback);
    }

    [Fact]
    public async Task CreateNewContainer_ComClsids_GrantedClsidsStoredInEntry()
    {
        var clsids = new List<string> { "{CLSID-A}", "{CLSID-B}" };
        _containerService.Setup(s => s.CreateProfile(It.IsAny<AppContainerEntry>()));
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        _containerService.Setup(s => s.GrantComAccess(It.IsAny<string>(), It.IsAny<string>()));

        var result = await CreateHandler().CreateNewContainer(
            "rfn_test", "Test", isEphemeral: false, [], loopback: false, clsids);

        Assert.NotNull(result.Entry);
        Assert.NotNull(result.Entry!.ComAccessClsids);
        Assert.Contains("{CLSID-A}", result.Entry.ComAccessClsids!);
        Assert.Contains("{CLSID-B}", result.Entry.ComAccessClsids!);
        Assert.Empty(result.ComErrors);
    }

    [Fact]
    public async Task CreateNewContainer_ComGrantFailsForOneClsid_PartialSuccess()
    {
        var clsids = new List<string> { "{CLSID-OK}", "{CLSID-FAIL}" };
        _containerService.Setup(s => s.CreateProfile(It.IsAny<AppContainerEntry>()));
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        _containerService.Setup(s => s.GrantComAccess(It.IsAny<string>(), "{CLSID-OK}"));
        _containerService.Setup(s => s.GrantComAccess(It.IsAny<string>(), "{CLSID-FAIL}"))
            .Throws(new UnauthorizedAccessException("denied"));

        var result = await CreateHandler().CreateNewContainer(
            "rfn_test", "Test", isEphemeral: false, [], loopback: false, clsids);

        Assert.NotNull(result.Entry);
        Assert.Contains("{CLSID-OK}", result.Entry!.ComAccessClsids!);
        Assert.DoesNotContain("{CLSID-FAIL}", result.Entry.ComAccessClsids!);
        Assert.Single(result.ComErrors);
    }

    // ── ApplyEditChanges ────────────────────────────────────────────────────

    private AppContainerEntry MakeExisting(string name = "rfn_test") => new()
    {
        Name = name,
        DisplayName = "Original",
        Capabilities = ["S-1-15-3-1"],
        EnableLoopback = false,
        ComAccessClsids = null
    };

    [Fact]
    public async Task ApplyEditChanges_DisplayNameUpdated()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        var existing = MakeExisting();

        await CreateHandler().ApplyEditChanges(existing, "New Name", existing.Capabilities!, false, [], false);

        Assert.Equal("New Name", existing.DisplayName);
    }

    [Fact]
    public async Task ApplyEditChanges_CapabilitiesChanged_ResultIndicatesChange()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        var existing = MakeExisting();

        var result = await CreateHandler().ApplyEditChanges(
            existing, existing.DisplayName,
            ["S-1-15-3-1", "S-1-15-3-2"],
            false, [], false);

        Assert.True(result.CapabilitiesChanged);
    }

    [Fact]
    public async Task ApplyEditChanges_CapabilitiesUnchanged_ResultIndicatesNoChange()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        var existing = new AppContainerEntry
        {
            Name = "rfn_test",
            DisplayName = "Original",
            Capabilities = ["S-1-15-3-1", "S-1-15-3-2"],
            EnableLoopback = false,
        };

        // Same set in reverse order — comparison is order-insensitive, must report no change
        var result = await CreateHandler().ApplyEditChanges(
            existing, existing.DisplayName,
            ["S-1-15-3-2", "S-1-15-3-1"],
            false, [], false);

        Assert.False(result.CapabilitiesChanged);
    }

    [Fact]
    public async Task ApplyEditChanges_LoopbackToggleSucceeds_EnableLoopbackUpdated()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        _containerService.Setup(s => s.SetLoopbackExemption("rfn_test", true)).ReturnsAsync(true);
        var existing = MakeExisting();

        var result = await CreateHandler().ApplyEditChanges(existing, existing.DisplayName, existing.Capabilities!, true, [], false);

        Assert.False(result.LoopbackFailed);
        Assert.True(existing.EnableLoopback);
    }

    [Fact]
    public async Task ApplyEditChanges_LoopbackToggleFails_LoopbackFailedAndValuePreserved()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        _containerService.Setup(s => s.SetLoopbackExemption("rfn_test", true)).ReturnsAsync(false);
        var existing = MakeExisting(); // EnableLoopback = false

        var result = await CreateHandler().ApplyEditChanges(existing, existing.DisplayName, existing.Capabilities!, true, [], false);

        Assert.True(result.LoopbackFailed);
        Assert.Equal("enable", result.LoopbackFailAction);
        Assert.False(existing.EnableLoopback); // unchanged
    }

    [Fact]
    public async Task ApplyEditChanges_ComAdd_NewClsidGrantedAndStoredInEntry()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        _containerService.Setup(s => s.GrantComAccess(It.IsAny<string>(), "{CLSID-NEW}"));
        var existing = MakeExisting();

        var result = await CreateHandler().ApplyEditChanges(
            existing, existing.DisplayName, existing.Capabilities!, false,
            ["{CLSID-NEW}"], false);

        Assert.NotNull(existing.ComAccessClsids);
        Assert.Contains("{CLSID-NEW}", existing.ComAccessClsids!);
        Assert.Empty(result.ComErrors);
    }

    [Fact]
    public async Task ApplyEditChanges_ComRemove_RevokedClsidRemovedFromEntry()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        _containerService.Setup(s => s.RevokeComAccess(It.IsAny<string>(), "{CLSID-OLD}"));
        var existing = MakeExisting();
        existing.ComAccessClsids = ["{CLSID-OLD}"];

        var result = await CreateHandler().ApplyEditChanges(
            existing, existing.DisplayName, existing.Capabilities!, false,
            [], false);

        Assert.Null(existing.ComAccessClsids); // list becomes null when empty
        Assert.Empty(result.ComErrors);
    }

    [Fact]
    public async Task ApplyEditChanges_ComRevokeFails_ErrorReportedAndClsidKeptInEntry()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        _containerService.Setup(s => s.RevokeComAccess(It.IsAny<string>(), "{CLSID-OLD}"))
            .Throws(new UnauthorizedAccessException("denied"));
        var existing = MakeExisting();
        existing.ComAccessClsids = ["{CLSID-OLD}"];

        var result = await CreateHandler().ApplyEditChanges(
            existing, existing.DisplayName, existing.Capabilities!, false,
            [], false);

        Assert.Single(result.ComErrors);
        Assert.NotNull(existing.ComAccessClsids);
        Assert.Contains("{CLSID-OLD}", existing.ComAccessClsids!); // still there
    }

    // ── ApplyEditChanges: Ephemeral toggle ──────────────────────────────────

    [Fact]
    public async Task ApplyEditChanges_EphemeralToggledOn_SetsIsEphemeralAndDeleteAfterUtc()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        var existing = MakeExisting(); // IsEphemeral = false by default
        var before = DateTime.UtcNow;

        await CreateHandler().ApplyEditChanges(existing, existing.DisplayName, existing.Capabilities!, false, [], isEphemeral: true);

        var after = DateTime.UtcNow;
        Assert.True(existing.IsEphemeral);
        Assert.NotNull(existing.DeleteAfterUtc);
        Assert.InRange(existing.DeleteAfterUtc!.Value, before.AddHours(24).AddSeconds(-5), after.AddHours(24).AddSeconds(5));
    }

    [Fact]
    public async Task ApplyEditChanges_EphemeralToggledOff_ClearsIsEphemeralAndDeleteAfterUtc()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        var existing = MakeExisting();
        existing.IsEphemeral = true;
        existing.DeleteAfterUtc = DateTime.UtcNow.AddHours(12);

        await CreateHandler().ApplyEditChanges(existing, existing.DisplayName, existing.Capabilities!, false, [], isEphemeral: false);

        Assert.False(existing.IsEphemeral);
        Assert.Null(existing.DeleteAfterUtc);
    }

    [Fact]
    public async Task ApplyEditChanges_EphemeralUnchanged_DoesNotModifyDeleteAfterUtc()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        var originalExpiry = DateTime.UtcNow.AddHours(12);
        var existing = MakeExisting();
        existing.IsEphemeral = true;
        existing.DeleteAfterUtc = originalExpiry;

        await CreateHandler().ApplyEditChanges(existing, existing.DisplayName, existing.Capabilities!, false, [], isEphemeral: true);

        Assert.True(existing.IsEphemeral);
        Assert.Equal(originalExpiry, existing.DeleteAfterUtc); // not reset to +24h
    }

    // ── F-83: Non-empty capabilities stored in entry ─────────────────────────────

    [Fact]
    public async Task CreateNewContainer_NonEmptyCapabilities_StoredInEntry()
    {
        // Arrange: create with a non-empty capabilities list
        var capabilities = new List<string> { "S-1-15-3-1", "S-1-15-3-2" };
        _containerService.Setup(s => s.CreateProfile(It.IsAny<AppContainerEntry>()));
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");

        // Act
        var result = await CreateHandler().CreateNewContainer(
            "rfn_test", "Test", isEphemeral: false, capabilities, loopback: false, []);

        // Assert: capabilities correctly stored in entry
        Assert.NotNull(result.Entry);
        Assert.NotNull(result.Entry!.Capabilities);
        Assert.Equal(2, result.Entry.Capabilities!.Count);
        Assert.Contains("S-1-15-3-1", result.Entry.Capabilities);
        Assert.Contains("S-1-15-3-2", result.Entry.Capabilities);
    }

    [Fact]
    public async Task ApplyEditChanges_NonEmptyCapabilities_StoredInEntry()
    {
        // Arrange: existing entry has one capability; update to two capabilities
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        var existing = MakeExisting(); // existing has ["S-1-15-3-1"]

        var newCapabilities = new List<string> { "S-1-15-3-1", "S-1-15-3-2", "S-1-15-3-3" };

        // Act
        var result = await CreateHandler().ApplyEditChanges(
            existing, existing.DisplayName, newCapabilities, false, [], false);

        // Assert: capabilities updated and stored correctly
        Assert.NotNull(existing.Capabilities);
        Assert.Equal(3, existing.Capabilities!.Count);
        Assert.Contains("S-1-15-3-1", existing.Capabilities);
        Assert.Contains("S-1-15-3-2", existing.Capabilities);
        Assert.Contains("S-1-15-3-3", existing.Capabilities);
        Assert.True(result.CapabilitiesChanged);
    }
}