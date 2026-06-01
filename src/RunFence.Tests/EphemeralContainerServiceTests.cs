using Moq;
using RunFence.Account;
using RunFence.Account.Lifecycle;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Tests.TestDoubles;
using Xunit;

namespace RunFence.Tests;

public class EphemeralContainerServiceTests : IDisposable
{
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IProcessListService> _processListService = new();
    private readonly Mock<ITrayBalloonService> _trayBalloon = new();
    private readonly RecordingEphemeralContainerDeletionService _containerDeletion = new();
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);
    private readonly List<SessionContext> _sessions = [];

    public EphemeralContainerServiceTests()
    {
        _processListService
            .Setup(p => p.GetSidsWithProcesses(It.IsAny<IEnumerable<string>>()))
            .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        foreach (var session in _sessions)
            session.Dispose();

        _pinKey.Dispose();
    }

    private SessionContext CreateSession(AppDatabase database)
    {
        var session = new SessionContext
{
            Database = database,
            CredentialStore = new CredentialStore(),
        }.WithClonedPinDerivedKey(_pinKey);
        _sessions.Add(session);
        return session;
    }

    private static AppContainerEntry ExpiredContainer(string name, string? sid = null) =>
        new() { Name = name, IsEphemeral = true, DeleteAfterUtc = DateTime.UtcNow.AddHours(-1), Sid = sid ?? string.Empty };

    private static AppContainerEntry OrphanedContainer(string name) =>
        new() { Name = name, IsEphemeral = true };

    private EphemeralContainerService CreateStartedService(
        AppDatabase database,
        IContainerDeletionService? containerDeletion = null)
    {
        if (containerDeletion == null)
        {
            _containerDeletion.Reset();
            _containerDeletion.ApplyDeletionToDatabase = (db, entry) =>
            {
                db.AppContainers.Remove(entry);
                db.Apps.RemoveAll(app => app.AppContainerName == entry.Name);
            };
            _containerDeletion.Delete = (entry, sid) =>
            {
                _containerDeletion.ApplyDeletionToDatabase?.Invoke(database, entry);
                return ContainerDeletionResult.Success();
            };
            containerDeletion = _containerDeletion;
        }

        var service = new EphemeralContainerService(
            containerDeletion,
            _databaseService.Object,
            _log.Object,
            new LambdaSessionProvider(() => CreateSession(database)),
            new InlineUiThreadInvoker(a => a()),
            _processListService.Object,
            _trayBalloon.Object);
        service.Start();
        return service;
    }

    [Fact]
    public async Task ProcessExpiredContainers_RemovesOrphanedEntries()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(OrphanedContainer("ram_orphan"));

        using var service = CreateStartedService(database);
        await service.ProcessExpiredContainers();

        Assert.Empty(database.AppContainers);
        Assert.Equal(["ram_orphan"], _containerDeletion.DeletedContainerNames);
        _databaseService.Verify(s => s.SaveConfig(database, It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public async Task ProcessExpiredContainers_DeletesExpiredContainerAndCallsService()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(ExpiredContainer("ram_expired"));

        using var service = CreateStartedService(database);
        await service.ProcessExpiredContainers();

        Assert.Empty(database.AppContainers);
        Assert.Equal(["ram_expired"], _containerDeletion.DeletedContainerNames);
        _databaseService.Verify(s => s.SaveConfig(database, It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public async Task ProcessExpiredContainers_RemovesReferencingAppEntries()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(ExpiredContainer("ram_expired"));
        database.Apps.Add(new AppEntry { Name = "SandboxedApp", AppContainerName = "ram_expired" });
        database.Apps.Add(new AppEntry { Name = "OtherApp", AppContainerName = "ram_other" });

        using var service = CreateStartedService(database);
        await service.ProcessExpiredContainers();

        Assert.Single(database.Apps);
        Assert.Equal("OtherApp", database.Apps[0].Name);
    }

    [Fact]
    public async Task ProcessExpiredContainers_SkipsOnDeleteFailure_PreservesEntry()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(ExpiredContainer("ram_fail"));

        var containerDeletion = new RecordingEphemeralContainerDeletionService
        {
            Delete = (_, _) => ContainerDeletionResult.Failure("delete failed")
        };

        using var service = CreateStartedService(database, containerDeletion);
        await service.ProcessExpiredContainers();

        Assert.Single(database.AppContainers);
        Assert.Equal(["ram_fail"], containerDeletion.DeletedContainerNames);
        _databaseService.Verify(s => s.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredContainers_NonEphemeral_NotTouched()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry { Name = "ram_permanent" });

        using var service = CreateStartedService(database);
        await service.ProcessExpiredContainers();

        Assert.Single(database.AppContainers);
        Assert.Empty(_containerDeletion.DeletedContainerNames);
        _databaseService.Verify(s => s.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredAtStartup_RemovesExpiredContainersAndReferencingApps()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(ExpiredContainer("ram_expired"));
        database.Apps.Add(new AppEntry { Name = "App", AppContainerName = "ram_expired" });

        _containerDeletion.Reset();
        _containerDeletion.Delete = (entry, _) =>
        {
            _containerDeletion.ApplyDeletionToDatabase?.Invoke(database, entry);
            return ContainerDeletionResult.Success();
        };

        var result = await EphemeralContainerService.ProcessExpiredAtStartup(
            database, _containerDeletion, _log.Object, _processListService.Object);

        Assert.True(result.Changed);
        Assert.Empty(result.Warnings);
        Assert.Empty(database.AppContainers);
        Assert.Empty(database.Apps);
        Assert.Equal(["ram_expired"], _containerDeletion.DeletedContainerNames);
    }

    [Fact]
    public async Task ProcessExpiredAtStartup_RemovesOrphanedEntries()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(OrphanedContainer("ram_orphan"));

        _containerDeletion.Reset();
        _containerDeletion.Delete = (entry, _) =>
        {
            _containerDeletion.ApplyDeletionToDatabase?.Invoke(database, entry);
            return ContainerDeletionResult.Success();
        };

        var result = await EphemeralContainerService.ProcessExpiredAtStartup(
            database, _containerDeletion, _log.Object, _processListService.Object);

        Assert.True(result.Changed);
        Assert.Empty(result.Warnings);
        Assert.Empty(database.AppContainers);
        Assert.Equal(["ram_orphan"], _containerDeletion.DeletedContainerNames);
    }

    [Fact]
    public async Task ProcessExpiredAtStartup_EmptyList_ReturnsFalse()
    {
        var database = new AppDatabase();

        var result = await EphemeralContainerService.ProcessExpiredAtStartup(
            database, _containerDeletion, _log.Object, _processListService.Object);

        Assert.False(result.Changed);
        Assert.Empty(result.Warnings);
        Assert.Empty(_containerDeletion.DeletedContainerNames);
    }

    [Fact]
    public async Task ProcessExpiredAtStartup_NonExpiredEntry_NotTouched()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry
        {
            Name = "ram_future",
            IsEphemeral = true,
            DeleteAfterUtc = DateTime.UtcNow.AddHours(12)
        });

        var result = await EphemeralContainerService.ProcessExpiredAtStartup(
            database, _containerDeletion, _log.Object, _processListService.Object);

        Assert.False(result.Changed);
        Assert.Empty(result.Warnings);
        Assert.Single(database.AppContainers);
        Assert.Empty(_containerDeletion.DeletedContainerNames);
    }

    [Fact]
    public async Task ProcessExpiredContainers_WhenEntriesRemoved_FiresContainersChanged()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(OrphanedContainer("ram_orphan"));

        using var service = CreateStartedService(database);
        var eventFired = false;
        service.ContainersChanged += () => eventFired = true;

        await service.ProcessExpiredContainers();

        Assert.True(eventFired);
    }

    [Fact]
    public async Task ProcessExpiredContainers_WhenTtlExtended_FiresContainersChanged()
    {
        var containerSid = "S-1-15-2-8888";
        var database = new AppDatabase();
        var entry = ExpiredContainer("ram_busy", containerSid);
        database.AppContainers.Add(entry);

        _processListService
            .Setup(p => p.GetSidsWithProcesses(It.IsAny<IEnumerable<string>>()))
            .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { containerSid });

        using var service = CreateStartedService(database);
        var eventFired = false;
        service.ContainersChanged += () => eventFired = true;

        await service.ProcessExpiredContainers();

        Assert.True(eventFired);
    }

    [Fact]
    public async Task ProcessExpiredContainers_WhenNothingChanged_DoesNotFireContainersChanged()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry { Name = "ram_permanent" });

        using var service = CreateStartedService(database);
        var eventFired = false;
        service.ContainersChanged += () => eventFired = true;

        await service.ProcessExpiredContainers();

        Assert.False(eventFired);
    }

    [Fact]
    public async Task ProcessExpiredAtStartup_DeleteFailure_EntryKept_ReturnsFalse()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(ExpiredContainer("ram_fail"));

        _containerDeletion.Reset();
        _containerDeletion.Delete = (_, _) => ContainerDeletionResult.Failure("delete failed");

        var result = await EphemeralContainerService.ProcessExpiredAtStartup(
            database, _containerDeletion, _log.Object, _processListService.Object);

        Assert.False(result.Changed);
        Assert.Empty(result.Warnings);
        Assert.Single(database.AppContainers);
        Assert.Equal(["ram_fail"], _containerDeletion.DeletedContainerNames);
    }

    [Fact]
    public async Task ProcessExpiredAtStartup_RunningProcesses_PostponesDeleteAndExtendsTtl()
    {
        var containerSid = "S-1-15-2-9999";
        var database = new AppDatabase();
        var entry = ExpiredContainer("ram_busy", containerSid);
        database.AppContainers.Add(entry);

        _processListService
            .Setup(p => p.GetSidsWithProcesses(It.IsAny<IEnumerable<string>>()))
            .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { containerSid });

        var result = await EphemeralContainerService.ProcessExpiredAtStartup(
            database, _containerDeletion, _log.Object, _processListService.Object);

        Assert.True(result.Changed);
        Assert.Empty(result.Warnings);
        Assert.Single(database.AppContainers);
        Assert.True(entry.DeleteAfterUtc > DateTime.UtcNow);
        Assert.Empty(_containerDeletion.DeletedContainerNames);
    }

    [Fact]
    public async Task ProcessExpiredContainers_RunningProcesses_PostponesDeleteAndExtendsTtl()
    {
        var containerSid = "S-1-15-2-9999";
        var database = new AppDatabase();
        var entry = ExpiredContainer("ram_busy", containerSid);
        database.AppContainers.Add(entry);

        _processListService
            .Setup(p => p.GetSidsWithProcesses(It.IsAny<IEnumerable<string>>()))
            .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { containerSid });

        using var service = CreateStartedService(database);
        await service.ProcessExpiredContainers();

        Assert.Single(database.AppContainers);
        Assert.True(entry.DeleteAfterUtc > DateTime.UtcNow);
        Assert.Empty(_containerDeletion.DeletedContainerNames);
        _databaseService.Verify(s => s.SaveConfig(database, It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public async Task ProcessExpiredContainers_DeleteWarnings_AreLoggedAndShown()
    {
        const string warning = "cleanup warning";
        var database = new AppDatabase();
        database.AppContainers.Add(ExpiredContainer("ram_warn"));
        _containerDeletion.Reset();
        _containerDeletion.Delete = (entry, _) =>
        {
            database.AppContainers.Remove(entry);
            return ContainerDeletionResult.Success([warning]);
        };

        using var service = CreateStartedService(database, _containerDeletion);
        await service.ProcessExpiredContainers();

        _trayBalloon.Verify(t => t.ShowWarning(warning), Times.Once);
    }

    [Fact]
    public async Task ProcessExpiredAtStartup_DeleteWarnings_ReturnsWarnings()
    {
        const string warning = "cleanup warning";
        var database = new AppDatabase();
        database.AppContainers.Add(ExpiredContainer("ram_warn"));
        _containerDeletion.Reset();
        _containerDeletion.Delete = (entry, _) =>
        {
            database.AppContainers.Remove(entry);
            return ContainerDeletionResult.Success([warning]);
        };

        var result = await EphemeralContainerService.ProcessExpiredAtStartup(
            database, _containerDeletion, _log.Object, _processListService.Object);

        Assert.True(result.Changed);
        Assert.Equal([warning], result.Warnings);
    }
}
