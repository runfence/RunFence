using Moq;
using RunFence.Account;
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
        var sidNameCache = new Mock<ISidNameCacheService>();
        return new(_containerService.Object, new LambdaDatabaseProvider(() => db), _log.Object, sidNameCache.Object);
    }

    // ── CreateNewContainer ──────────────────────────────────────────────────

    [Fact]
    public void CreateNewContainer_NonEphemeral_AddsEntryToDatabase()
    {
        var db = new AppDatabase();
        _containerService.Setup(s => s.CreateProfile(It.IsAny<AppContainerEntry>()));
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");

        var result = CreateHandler(db).CreateNewContainer(
            "rfn_test", "Test", isEphemeral: false,
            [], loopback: false, [],
            out _, out _, out _);

        Assert.NotNull(result);
        Assert.Single(db.AppContainers);
        Assert.False(result.IsEphemeral);
        Assert.Null(result.DeleteAfterUtc);
    }

    [Fact]
    public void CreateNewContainer_Ephemeral_SetsIsEphemeralAndDeleteAfterUtc()
    {
        var before = DateTime.UtcNow;
        _containerService.Setup(s => s.CreateProfile(It.IsAny<AppContainerEntry>()));
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");

        var result = CreateHandler().CreateNewContainer(
            "rfn_eabc123", "Temp", isEphemeral: true,
            [], loopback: false, [],
            out _, out _, out _);

        Assert.NotNull(result);
        Assert.True(result.IsEphemeral);
        Assert.NotNull(result.DeleteAfterUtc);
        var after = DateTime.UtcNow;
        Assert.InRange(result.DeleteAfterUtc!.Value, before.AddHours(24).AddSeconds(-5), after.AddHours(24).AddSeconds(5));
    }

    [Fact]
    public void CreateNewContainer_DuplicateProfileName_ReturnsNullWithValidationError()
    {
        var db = new AppDatabase();
        db.AppContainers.Add(new AppContainerEntry { Name = "rfn_test", DisplayName = "Existing" });

        var result = CreateHandler(db).CreateNewContainer(
            "rfn_test", "Another", isEphemeral: false,
            [], loopback: false, [],
            out var validationError, out _, out _);

        Assert.Null(result);
        Assert.NotNull(validationError);
        _containerService.Verify(s => s.CreateProfile(It.IsAny<AppContainerEntry>()), Times.Never);
    }

    [Fact]
    public void CreateNewContainer_ProfileCreationThrows_ReturnsNullWithCreationError_EntryNotAdded()
    {
        var db = new AppDatabase();
        _containerService.Setup(s => s.CreateProfile(It.IsAny<AppContainerEntry>()))
            .Throws(new InvalidOperationException("OS error"));

        var result = CreateHandler(db).CreateNewContainer(
            "rfn_test", "Test", isEphemeral: false,
            [], loopback: false, [],
            out _, out var creationError, out _);

        Assert.Null(result);
        Assert.NotNull(creationError);
        Assert.Empty(db.AppContainers);
    }

    [Fact]
    public void CreateNewContainer_LoopbackRequested_AndSucceeds_EnableLoopbackTrue()
    {
        _containerService.Setup(s => s.CreateProfile(It.IsAny<AppContainerEntry>()));
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        _containerService.Setup(s => s.SetLoopbackExemption(It.IsAny<string>(), true)).Returns(true);

        var result = CreateHandler().CreateNewContainer(
            "rfn_test", "Test", isEphemeral: false,
            [], loopback: true, [],
            out _, out _, out _);

        Assert.NotNull(result);
        Assert.True(result.EnableLoopback);
    }

    [Fact]
    public void CreateNewContainer_LoopbackRequested_ButFails_EnableLoopbackFalse()
    {
        _containerService.Setup(s => s.CreateProfile(It.IsAny<AppContainerEntry>()));
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        _containerService.Setup(s => s.SetLoopbackExemption(It.IsAny<string>(), true)).Returns(false);

        var result = CreateHandler().CreateNewContainer(
            "rfn_test", "Test", isEphemeral: false,
            [], loopback: true, [],
            out _, out _, out _);

        Assert.NotNull(result);
        Assert.False(result.EnableLoopback);
    }

    [Fact]
    public void CreateNewContainer_ComClsids_GrantedClsidsStoredInEntry()
    {
        var clsids = new List<string> { "{CLSID-A}", "{CLSID-B}" };
        _containerService.Setup(s => s.CreateProfile(It.IsAny<AppContainerEntry>()));
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        _containerService.Setup(s => s.GrantComAccess(It.IsAny<string>(), It.IsAny<string>()));

        var result = CreateHandler().CreateNewContainer(
            "rfn_test", "Test", isEphemeral: false,
            [], loopback: false, clsids,
            out _, out _, out var comErrors);

        Assert.NotNull(result);
        Assert.NotNull(result.ComAccessClsids);
        Assert.Contains("{CLSID-A}", result.ComAccessClsids!);
        Assert.Contains("{CLSID-B}", result.ComAccessClsids!);
        Assert.Empty(comErrors);
    }

    [Fact]
    public void CreateNewContainer_ComGrantFailsForOneClsid_PartialSuccess()
    {
        var clsids = new List<string> { "{CLSID-OK}", "{CLSID-FAIL}" };
        _containerService.Setup(s => s.CreateProfile(It.IsAny<AppContainerEntry>()));
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        _containerService.Setup(s => s.GrantComAccess(It.IsAny<string>(), "{CLSID-OK}"));
        _containerService.Setup(s => s.GrantComAccess(It.IsAny<string>(), "{CLSID-FAIL}"))
            .Throws(new UnauthorizedAccessException("denied"));

        var result = CreateHandler().CreateNewContainer(
            "rfn_test", "Test", isEphemeral: false,
            [], loopback: false, clsids,
            out _, out _, out var comErrors);

        Assert.NotNull(result);
        Assert.Contains("{CLSID-OK}", result.ComAccessClsids!);
        Assert.DoesNotContain("{CLSID-FAIL}", result.ComAccessClsids!);
        Assert.Single(comErrors);
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
    public void ApplyEditChanges_DisplayNameUpdated()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        var existing = MakeExisting();

        CreateHandler().ApplyEditChanges(existing, "New Name", existing.Capabilities!, false, [], false);

        Assert.Equal("New Name", existing.DisplayName);
    }

    [Fact]
    public void ApplyEditChanges_CapabilitiesChanged_ResultIndicatesChange()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        var existing = MakeExisting();

        var result = CreateHandler().ApplyEditChanges(
            existing, existing.DisplayName,
            ["S-1-15-3-1", "S-1-15-3-2"],
            false, [], false);

        Assert.True(result.CapabilitiesChanged);
    }

    [Fact]
    public void ApplyEditChanges_CapabilitiesUnchanged_ResultIndicatesNoChange()
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
        var result = CreateHandler().ApplyEditChanges(
            existing, existing.DisplayName,
            ["S-1-15-3-2", "S-1-15-3-1"],
            false, [], false);

        Assert.False(result.CapabilitiesChanged);
    }

    [Fact]
    public void ApplyEditChanges_LoopbackToggleSucceeds_EnableLoopbackUpdated()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        _containerService.Setup(s => s.SetLoopbackExemption("rfn_test", true)).Returns(true);
        var existing = MakeExisting();

        var result = CreateHandler().ApplyEditChanges(existing, existing.DisplayName, existing.Capabilities!, true, [], false);

        Assert.False(result.LoopbackFailed);
        Assert.True(existing.EnableLoopback);
    }

    [Fact]
    public void ApplyEditChanges_LoopbackToggleFails_LoopbackFailedAndValuePreserved()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        _containerService.Setup(s => s.SetLoopbackExemption("rfn_test", true)).Returns(false);
        var existing = MakeExisting(); // EnableLoopback = false

        var result = CreateHandler().ApplyEditChanges(existing, existing.DisplayName, existing.Capabilities!, true, [], false);

        Assert.True(result.LoopbackFailed);
        Assert.Equal("enable", result.LoopbackFailAction);
        Assert.False(existing.EnableLoopback); // unchanged
    }

    [Fact]
    public void ApplyEditChanges_ComAdd_NewClsidGrantedAndStoredInEntry()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        _containerService.Setup(s => s.GrantComAccess(It.IsAny<string>(), "{CLSID-NEW}"));
        var existing = MakeExisting();

        var result = CreateHandler().ApplyEditChanges(
            existing, existing.DisplayName, existing.Capabilities!, false,
            ["{CLSID-NEW}"], false);

        Assert.NotNull(existing.ComAccessClsids);
        Assert.Contains("{CLSID-NEW}", existing.ComAccessClsids!);
        Assert.Empty(result.ComErrors);
    }

    [Fact]
    public void ApplyEditChanges_ComRemove_RevokedClsidRemovedFromEntry()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        _containerService.Setup(s => s.RevokeComAccess(It.IsAny<string>(), "{CLSID-OLD}"));
        var existing = MakeExisting();
        existing.ComAccessClsids = ["{CLSID-OLD}"];

        var result = CreateHandler().ApplyEditChanges(
            existing, existing.DisplayName, existing.Capabilities!, false,
            [], false);

        Assert.Null(existing.ComAccessClsids); // list becomes null when empty
        Assert.Empty(result.ComErrors);
    }

    [Fact]
    public void ApplyEditChanges_ComRevokeFails_ErrorReportedAndClsidKeptInEntry()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        _containerService.Setup(s => s.RevokeComAccess(It.IsAny<string>(), "{CLSID-OLD}"))
            .Throws(new UnauthorizedAccessException("denied"));
        var existing = MakeExisting();
        existing.ComAccessClsids = ["{CLSID-OLD}"];

        var result = CreateHandler().ApplyEditChanges(
            existing, existing.DisplayName, existing.Capabilities!, false,
            [], false);

        Assert.Single(result.ComErrors);
        Assert.NotNull(existing.ComAccessClsids);
        Assert.Contains("{CLSID-OLD}", existing.ComAccessClsids!); // still there
    }

    // ── ApplyEditChanges: Ephemeral toggle ──────────────────────────────────

    [Fact]
    public void ApplyEditChanges_EphemeralToggledOn_SetsIsEphemeralAndDeleteAfterUtc()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        var existing = MakeExisting(); // IsEphemeral = false by default
        var before = DateTime.UtcNow;

        CreateHandler().ApplyEditChanges(existing, existing.DisplayName, existing.Capabilities!, false, [], isEphemeral: true);

        var after = DateTime.UtcNow;
        Assert.True(existing.IsEphemeral);
        Assert.NotNull(existing.DeleteAfterUtc);
        Assert.InRange(existing.DeleteAfterUtc!.Value, before.AddHours(24).AddSeconds(-5), after.AddHours(24).AddSeconds(5));
    }

    [Fact]
    public void ApplyEditChanges_EphemeralToggledOff_ClearsIsEphemeralAndDeleteAfterUtc()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        var existing = MakeExisting();
        existing.IsEphemeral = true;
        existing.DeleteAfterUtc = DateTime.UtcNow.AddHours(12);

        CreateHandler().ApplyEditChanges(existing, existing.DisplayName, existing.Capabilities!, false, [], isEphemeral: false);

        Assert.False(existing.IsEphemeral);
        Assert.Null(existing.DeleteAfterUtc);
    }

    [Fact]
    public void ApplyEditChanges_EphemeralUnchanged_DoesNotModifyDeleteAfterUtc()
    {
        _containerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
        var originalExpiry = DateTime.UtcNow.AddHours(12);
        var existing = MakeExisting();
        existing.IsEphemeral = true;
        existing.DeleteAfterUtc = originalExpiry;

        CreateHandler().ApplyEditChanges(existing, existing.DisplayName, existing.Capabilities!, false, [], isEphemeral: true);

        Assert.True(existing.IsEphemeral);
        Assert.Equal(originalExpiry, existing.DeleteAfterUtc); // not reset to +24h
    }
}