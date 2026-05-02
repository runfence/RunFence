using Moq;
using RunFence.Account;
using RunFence.Account.Lifecycle;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class EphemeralContainerServiceTests : IDisposable
{
    private readonly Mock<IContainerDeletionService> _containerDeletion = new();
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IProcessListService> _processListService = new();
    private readonly ProtectedBuffer _pinKey = new(new byte[32], protect: false);

    public EphemeralContainerServiceTests()
    {
        _processListService
            .Setup(p => p.GetSidsWithProcesses(It.IsAny<IEnumerable<string>>()))
            .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    public void Dispose() => _pinKey.Dispose();

    private SessionContext CreateSession(AppDatabase database) => new()
    {
        Database = database,
        CredentialStore = new CredentialStore(),
        PinDerivedKey = _pinKey
    };

    /// <summary>
    /// Creates an expired ephemeral container: <see cref="AppContainerEntry.IsEphemeral"/> = true,
    /// <see cref="AppContainerEntry.DeleteAfterUtc"/> = 1 hour ago.
    /// </summary>
    private static AppContainerEntry ExpiredContainer(string name, string? sid = null) =>
        new() { Name = name, IsEphemeral = true, DeleteAfterUtc = DateTime.UtcNow.AddHours(-1), Sid = sid ?? string.Empty };

    /// <summary>
    /// Creates an orphaned ephemeral container: <see cref="AppContainerEntry.IsEphemeral"/> = true,
    /// <see cref="AppContainerEntry.DeleteAfterUtc"/> = null (no expiry set, so immediately removable).
    /// </summary>
    private static AppContainerEntry OrphanedContainer(string name) =>
        new() { Name = name, IsEphemeral = true };

    /// <summary>
    /// Configures <see cref="_containerDeletion"/> to simulate successful deletion: returns true
    /// and removes the container entry (and any referencing app entries) from the database.
    /// </summary>
    private void SetupDeletionSucceeds(AppDatabase database)
    {
        _containerDeletion.Setup(s => s.DeleteContainer(It.IsAny<AppContainerEntry>(), It.IsAny<string?>()))
            .Returns((AppContainerEntry entry, string? _) =>
            {
                database.AppContainers.Remove(entry);
                database.Apps.RemoveAll(a => a.AppContainerName == entry.Name);
                return Task.FromResult(true);
            });
    }

    private EphemeralContainerService CreateStartedService(AppDatabase database,
        IContainerDeletionService? containerDeletion = null)
    {
        if (containerDeletion == null)
        {
            // Set up default mock to return true (success) and remove the container
            SetupDeletionSucceeds(database);
            containerDeletion = _containerDeletion.Object;
        }

        var service = new EphemeralContainerService(
            containerDeletion,
            _databaseService.Object, _log.Object,
            new LambdaSessionProvider(() => CreateSession(database)), new InlineUiThreadInvoker(a => a()),
            _processListService.Object);
        service.Start();
        return service;
    }

    // --- ProcessExpiredContainers ---

    [Fact]
    public async Task ProcessExpiredContainers_RemovesOrphanedEntries()
    {
        // Orphaned = IsEphemeral=true, DeleteAfterUtc=null (no expiry set)
        var database = new AppDatabase();
        database.AppContainers.Add(OrphanedContainer("ram_orphan"));

        using var service = CreateStartedService(database);
        await service.ProcessExpiredContainers();

        Assert.Empty(database.AppContainers);
        _containerDeletion.Verify(s => s.DeleteContainer(It.Is<AppContainerEntry>(e => e.Name == "ram_orphan"), It.IsAny<string?>()), Times.Once);
        _databaseService.Verify(s => s.SaveConfig(database, It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public async Task ProcessExpiredContainers_DeletesExpiredContainerAndCallsService()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(ExpiredContainer("ram_expired"));

        using var service = CreateStartedService(database);
        await service.ProcessExpiredContainers();

        Assert.Empty(database.AppContainers);
        _containerDeletion.Verify(s => s.DeleteContainer(It.Is<AppContainerEntry>(e => e.Name == "ram_expired"), It.IsAny<string?>()), Times.Once);
        _databaseService.Verify(s => s.SaveConfig(database, It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Once);
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

        // Only the expired container's app should be removed (via DeleteContainer mock behavior)
        Assert.Single(database.Apps);
        Assert.Equal("OtherApp", database.Apps[0].Name);
    }

    [Fact]
    public async Task ProcessExpiredContainers_SkipsOnDeleteFailure_PreservesEntry()
    {
        var database = new AppDatabase();
        var entry = ExpiredContainer("ram_fail");
        database.AppContainers.Add(entry);

        // Override: return false (failure) for this specific entry
        var containerDeletion = new Mock<IContainerDeletionService>();
        containerDeletion.Setup(s => s.DeleteContainer(It.IsAny<AppContainerEntry>(), It.IsAny<string?>()))
            .ReturnsAsync(false);

        using var service = CreateStartedService(database, containerDeletion.Object);
        await service.ProcessExpiredContainers();

        // Entry preserved when delete fails (retry on next tick)
        Assert.Single(database.AppContainers);
        _databaseService.Verify(s => s.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredContainers_NonEphemeral_NotTouched()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry { Name = "ram_permanent" }); // IsEphemeral defaults to false

        using var service = CreateStartedService(database);
        await service.ProcessExpiredContainers();

        Assert.Single(database.AppContainers);
        _containerDeletion.Verify(s => s.DeleteContainer(It.IsAny<AppContainerEntry>(), It.IsAny<string?>()), Times.Never);
        _databaseService.Verify(s => s.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredContainers_PassesContainerSidToDeleteContainer()
    {
        var containerSid = "S-1-15-2-1234567890";
        var database = new AppDatabase();
        database.AppContainers.Add(ExpiredContainer("ram_expired", containerSid));

        using var service = CreateStartedService(database);
        await service.ProcessExpiredContainers();

        _containerDeletion.Verify(s => s.DeleteContainer(It.IsAny<AppContainerEntry>(), containerSid), Times.Once);
    }

    // --- ProcessExpiredAtStartup ---

    [Fact]
    public async Task ProcessExpiredAtStartup_RemovesExpiredContainersAndReferencingApps()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(ExpiredContainer("ram_expired"));
        database.Apps.Add(new AppEntry { Name = "App", AppContainerName = "ram_expired" });

        SetupDeletionSucceeds(database);

        var changed = await EphemeralContainerService.ProcessExpiredAtStartup(
            database, _containerDeletion.Object, _log.Object, _processListService.Object);

        Assert.True(changed);
        Assert.Empty(database.AppContainers);
        Assert.Empty(database.Apps);
        _containerDeletion.Verify(s => s.DeleteContainer(It.Is<AppContainerEntry>(e => e.Name == "ram_expired"), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ProcessExpiredAtStartup_RemovesOrphanedEntries()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(OrphanedContainer("ram_orphan"));

        SetupDeletionSucceeds(database);

        var changed = await EphemeralContainerService.ProcessExpiredAtStartup(
            database, _containerDeletion.Object, _log.Object, _processListService.Object);

        Assert.True(changed);
        Assert.Empty(database.AppContainers);
        _containerDeletion.Verify(s => s.DeleteContainer(It.Is<AppContainerEntry>(e => e.Name == "ram_orphan"), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ProcessExpiredAtStartup_EmptyList_ReturnsFalse()
    {
        var database = new AppDatabase();

        var changed = await EphemeralContainerService.ProcessExpiredAtStartup(
            database, _containerDeletion.Object, _log.Object, _processListService.Object);

        Assert.False(changed);
    }

    [Fact]
    public async Task ProcessExpiredAtStartup_NonExpiredEntry_NotTouched()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry
        {
            Name = "ram_future", IsEphemeral = true,
            DeleteAfterUtc = DateTime.UtcNow.AddHours(12) // not yet expired
        });

        var changed = await EphemeralContainerService.ProcessExpiredAtStartup(
            database, _containerDeletion.Object, _log.Object, _processListService.Object);

        Assert.False(changed);
        Assert.Single(database.AppContainers);
    }

    // --- ContainersChanged event ---

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
        // A running-process postpone updates DeleteAfterUtc (TTL extension) and must fire ContainersChanged
        // so the UI reflects the updated expiry time.
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
        // No ephemeral entries → nothing to process
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry { Name = "ram_permanent" }); // IsEphemeral defaults to false

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

        _containerDeletion.Setup(s => s.DeleteContainer(It.IsAny<AppContainerEntry>(), It.IsAny<string?>()))
            .ReturnsAsync(false);

        var changed = await EphemeralContainerService.ProcessExpiredAtStartup(
            database, _containerDeletion.Object, _log.Object, _processListService.Object);

        // Deletion failed → entry preserved, changed=false
        Assert.False(changed);
        Assert.Single(database.AppContainers);
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

        var changed = await EphemeralContainerService.ProcessExpiredAtStartup(
            database, _containerDeletion.Object, _log.Object, _processListService.Object);

        // Entry preserved — postponed with extended TTL, changed=true (TTL was updated)
        Assert.True(changed);
        Assert.Single(database.AppContainers);
        Assert.True(entry.DeleteAfterUtc > DateTime.UtcNow);
        _containerDeletion.Verify(s => s.DeleteContainer(It.IsAny<AppContainerEntry>(), It.IsAny<string?>()), Times.Never);
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

        // Entry preserved — postponed with extended TTL
        Assert.Single(database.AppContainers);
        Assert.True(entry.DeleteAfterUtc > DateTime.UtcNow);
        _containerDeletion.Verify(s => s.DeleteContainer(It.IsAny<AppContainerEntry>(), It.IsAny<string?>()), Times.Never);
        _databaseService.Verify(s => s.SaveConfig(database, It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Once);
    }
}
