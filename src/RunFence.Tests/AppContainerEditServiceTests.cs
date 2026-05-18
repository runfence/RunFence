using Moq;
using RunFence.Account.UI.AppContainer;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class AppContainerEditServiceTests : IDisposable
{
    private readonly Mock<IAppContainerService> _containerService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<ISessionProvider> _sessionProvider = new();
    private readonly SecureSecret _pinBuffer = TestSecretFactory.Create(32);

    public void Dispose() => _pinBuffer.Dispose();

    [Fact]
    public async Task CreateNewContainer_SuccessfulCreate_PersistsAndReturnsEntry()
    {
        var db = new AppDatabase();
        var saveCount = 0;
        _databaseService
            .Setup(s => s.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
            .Callback(() => saveCount++);
        _containerService
            .Setup(s => s.CreateProfile(It.IsAny<AppContainerEntry>()))
            .Returns(AppContainerProfileSetupResult.Success(profileCreatedOrAlreadyExists: true));
        _containerService.Setup(s => s.GetSid("rfn_demo")).Returns("S-1-15-2-7");

        var result = await CreateService(db).CreateNewContainer("rfn_demo", "Demo", false, [], false, []);

        Assert.Equal(AppContainerOperationStatus.Succeeded, result.Status);
        var entry = Assert.Single(db.AppContainers);
        Assert.Same(entry, result.Entry);
        Assert.Equal("Demo", entry.DisplayName);
        Assert.Equal("S-1-15-2-7", entry.Sid);
        Assert.Null(entry.LifecycleState);
        Assert.Equal(2, saveCount);
        _containerService.Verify(s => s.CreateProfile(entry), Times.Once);
    }

    [Fact]
    public async Task CreateNewContainer_DuplicateProfileValidation_ReturnsSystemFailedWithoutMutation()
    {
        var db = new AppDatabase();
        db.AppContainers.Add(new AppContainerEntry { Name = "rfn_demo", DisplayName = "Existing" });

        var result = await CreateService(db).CreateNewContainer("rfn_demo", "Duplicate", false, [], false, []);

        Assert.Equal(AppContainerOperationStatus.SystemFailed, result.Status);
        Assert.Null(result.Entry);
        Assert.Equal("A container with profile name 'rfn_demo' already exists.", result.ErrorMessage);
        Assert.Single(db.AppContainers);
        _databaseService.Verify(
            s => s.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()),
            Times.Never);
        _containerService.Verify(s => s.CreateProfile(It.IsAny<AppContainerEntry>()), Times.Never);
    }

    [Fact]
    public async Task CreateNewContainer_ProfileCreationRollback_RemovesEntryAndDeletesProfile()
    {
        var db = new AppDatabase();
        var saveCount = 0;
        _databaseService
            .Setup(s => s.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
            .Callback(() => saveCount++);
        _containerService
            .Setup(s => s.CreateProfile(It.IsAny<AppContainerEntry>()))
            .Returns(AppContainerProfileSetupResult.Success(profileCreatedOrAlreadyExists: true));
        _containerService.Setup(s => s.GetSid("rfn_demo")).Returns("S-1-15-2-7");
        _containerService
            .Setup(s => s.GrantComAccess("S-1-15-2-7", "{CLSID-1}"))
            .Returns(AppContainerComAccessResult.Failure("grant failed"));

        var result = await CreateService(db).CreateNewContainer("rfn_demo", "Demo", false, [], false, ["{CLSID-1}"]);

        Assert.Equal(AppContainerOperationStatus.SystemFailed, result.Status);
        Assert.Null(result.Entry);
        Assert.Equal("grant failed", result.ErrorMessage);
        Assert.Empty(db.AppContainers);
        Assert.Equal(2, saveCount);
        _containerService.Verify(s => s.DeleteProfile("rfn_demo", false), Times.Once);
    }

    [Fact]
    public async Task CreateNewContainer_RollbackFailure_ReturnsCleanupPendingStatus()
    {
        var db = new AppDatabase();
        _containerService
            .Setup(s => s.CreateProfile(It.IsAny<AppContainerEntry>()))
            .Returns(AppContainerProfileSetupResult.Success(profileCreatedOrAlreadyExists: true));
        _containerService
            .Setup(s => s.SetLoopbackExemption("rfn_demo", true))
            .ReturnsAsync(false);
        _containerService
            .Setup(s => s.DeleteProfile("rfn_demo", false))
            .ThrowsAsync(new InvalidOperationException("rollback failed"));

        var result = await CreateService(db).CreateNewContainer("rfn_demo", "Demo", false, [], true, []);

        Assert.Equal(AppContainerOperationStatus.CleanupPending, result.Status);
        var entry = Assert.Single(db.AppContainers);
        Assert.Same(entry, result.Entry);
        Assert.Equal("CleanupPending", entry.LifecycleState);
        Assert.NotNull(entry.DeleteAfterUtc);
    }

    [Fact]
    public async Task ApplyEditChanges_DisplayNameEdit_UpdatesEntryAndSaves()
    {
        var existing = MakeExisting();
        var saveCount = 0;
        _databaseService
            .Setup(s => s.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
            .Callback(() => saveCount++);

        var result = await CreateService().ApplyEditChanges(
            existing,
            "Renamed",
            ["S-1-15-3-1"],
            false,
            [],
            false);

        Assert.Equal(AppContainerOperationStatus.Succeeded, result.Status);
        Assert.Equal("Renamed", existing.DisplayName);
        Assert.False(result.CapabilitiesChanged);
        Assert.Equal(1, saveCount);
    }

    [Fact]
    public async Task ApplyEditChanges_CapabilityChanges_ReportsAndUpdatesCapabilities()
    {
        var existing = MakeExisting();

        var result = await CreateService().ApplyEditChanges(
            existing,
            existing.DisplayName,
            ["S-1-15-3-2"],
            false,
            [],
            false);

        Assert.Equal(AppContainerOperationStatus.Succeeded, result.Status);
        Assert.True(result.CapabilitiesChanged);
        Assert.Equal(["S-1-15-3-2"], existing.Capabilities);
    }

    [Fact]
    public async Task ApplyEditChanges_EphemeralToggle_UpdatesDeleteAfterUtcForBothStates()
    {
        var existing = MakeExisting();

        var enableResult = await CreateService().ApplyEditChanges(
            existing,
            existing.DisplayName,
            existing.Capabilities!,
            false,
            [],
            true);

        Assert.Equal(AppContainerOperationStatus.Succeeded, enableResult.Status);
        Assert.True(existing.IsEphemeral);
        Assert.NotNull(existing.DeleteAfterUtc);

        var disableResult = await CreateService().ApplyEditChanges(
            existing,
            existing.DisplayName,
            existing.Capabilities!,
            false,
            [],
            false);

        Assert.Equal(AppContainerOperationStatus.Succeeded, disableResult.Status);
        Assert.False(existing.IsEphemeral);
        Assert.Null(existing.DeleteAfterUtc);
    }

    [Fact]
    public async Task ApplyEditChanges_ComGrantAndRevoke_RunOnCorrectSidesOfSave()
    {
        var existing = MakeExisting(comAccessClsids: ["{OLD}"]);
        var events = new List<string>();
        _databaseService
            .Setup(s => s.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
            .Callback(() => events.Add("save"));
        _containerService.Setup(s => s.GetSid(existing.Name)).Returns("S-1-15-2-7");
        _containerService
            .Setup(s => s.RevokeComAccess("S-1-15-2-7", "{OLD}"))
            .Callback(() => events.Add("revoke"))
            .Returns(AppContainerComAccessResult.Success());
        _containerService
            .Setup(s => s.GrantComAccess("S-1-15-2-7", "{NEW}"))
            .Callback(() => events.Add("grant"))
            .Returns(AppContainerComAccessResult.Success());

        var result = await CreateService().ApplyEditChanges(
            existing,
            existing.DisplayName,
            existing.Capabilities!,
            false,
            ["{NEW}"],
            false);

        Assert.Equal(AppContainerOperationStatus.Succeeded, result.Status);
        Assert.Equal(["revoke", "save", "grant"], events);
        Assert.Equal(["{NEW}"], existing.ComAccessClsids);
    }

    [Fact]
    public async Task ApplyEditChanges_DisablingLoopbackFailure_ReturnsCleanupPendingStatus()
    {
        var existing = MakeExisting(enableLoopback: true);
        _containerService.Setup(s => s.SetLoopbackExemption(existing.Name, false)).ReturnsAsync(false);

        var result = await CreateService().ApplyEditChanges(
            existing,
            existing.DisplayName,
            existing.Capabilities!,
            false,
            [],
            false);

        Assert.Equal(AppContainerOperationStatus.CleanupPending, result.Status);
        Assert.Contains("Loopback exemption", result.Warnings);
        _databaseService.Verify(
            s => s.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()),
            Times.Never);
    }

    private AppContainerEditService CreateService(AppDatabase? database = null)
    {
        var db = database ?? new AppDatabase();
        _sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
{
            Database = db,
            CredentialStore = new CredentialStore(),
        }.WithOwnedPinDerivedKey(_pinBuffer));

        return new AppContainerEditService(
            _containerService.Object,
            new LambdaDatabaseProvider(() => db),
            _log.Object,
            _databaseService.Object,
            _sessionProvider.Object);
    }

    private static AppContainerEntry MakeExisting(
        string name = "rfn_demo",
        bool enableLoopback = false,
        List<string>? comAccessClsids = null) => new()
    {
        Name = name,
        DisplayName = "Original",
        Capabilities = ["S-1-15-3-1"],
        EnableLoopback = enableLoopback,
        ComAccessClsids = comAccessClsids
    };
}
