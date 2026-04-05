using Moq;
using RunFence.Account.Lifecycle;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Container;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class EphemeralContainerServiceTests : IDisposable
{
    private readonly Mock<IAppContainerService> _containerService = new();
    private readonly Mock<IContainerDeletionService> _containerDeletion = new();
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly ProtectedBuffer _pinKey = new(new byte[32], protect: false);

    public void Dispose() => _pinKey.Dispose();

    private SessionContext CreateSession(AppDatabase database) => new()
    {
        Database = database,
        CredentialStore = new CredentialStore(),
        PinDerivedKey = _pinKey
    };

    private EphemeralContainerService CreateStartedService(AppDatabase database,
        IContainerDeletionService? containerDeletion = null)
    {
        containerDeletion ??= _containerDeletion.Object;
        // Set up mock to return true (success) by default
        _containerDeletion.Setup(s => s.DeleteContainer(It.IsAny<AppContainerEntry>(), It.IsAny<string?>()))
            .Returns((AppContainerEntry entry, string? _) =>
            {
                database.AppContainers.Remove(entry);
                database.Apps.RemoveAll(a => a.AppContainerName == entry.Name);
                return true;
            });
        var service = new EphemeralContainerService(
            _containerService.Object, containerDeletion,
            _databaseService.Object, _log.Object,
            new LambdaSessionProvider(() => CreateSession(database)), new InlineUiThreadInvoker(a => a()));
        service.Start();
        return service;
    }

    // --- ProcessExpiredContainers ---

    [Fact]
    public void ProcessExpiredContainers_RemovesOrphanedEntries()
    {
        // Orphaned = IsEphemeral=true, DeleteAfterUtc=null (no expiry set)
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry { Name = "ram_orphan", IsEphemeral = true });

        using var service = CreateStartedService(database);
        service.ProcessExpiredContainers();

        Assert.Empty(database.AppContainers);
        _containerDeletion.Verify(s => s.DeleteContainer(It.Is<AppContainerEntry>(e => e.Name == "ram_orphan"), It.IsAny<string?>()), Times.Once);
        _databaseService.Verify(s => s.SaveConfig(database, It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public void ProcessExpiredContainers_DeletesExpiredContainerAndCallsService()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry
        {
            Name = "ram_expired",
            IsEphemeral = true,
            DeleteAfterUtc = DateTime.UtcNow.AddHours(-1)
        });

        using var service = CreateStartedService(database);
        service.ProcessExpiredContainers();

        Assert.Empty(database.AppContainers);
        _containerDeletion.Verify(s => s.DeleteContainer(It.Is<AppContainerEntry>(e => e.Name == "ram_expired"), It.IsAny<string?>()), Times.Once);
        _databaseService.Verify(s => s.SaveConfig(database, It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public void ProcessExpiredContainers_RemovesReferencingAppEntries()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry
        {
            Name = "ram_expired",
            IsEphemeral = true,
            DeleteAfterUtc = DateTime.UtcNow.AddHours(-1)
        });
        database.Apps.Add(new AppEntry { Name = "SandboxedApp", AppContainerName = "ram_expired" });
        database.Apps.Add(new AppEntry { Name = "OtherApp", AppContainerName = "ram_other" });

        using var service = CreateStartedService(database);
        service.ProcessExpiredContainers();

        // Only the expired container's app should be removed (via DeleteContainer mock behavior)
        Assert.Single(database.Apps);
        Assert.Equal("OtherApp", database.Apps[0].Name);
    }

    [Fact]
    public void ProcessExpiredContainers_SkipsOnDeleteFailure_PreservesEntry()
    {
        var database = new AppDatabase();
        var entry = new AppContainerEntry
        {
            Name = "ram_fail",
            IsEphemeral = true,
            DeleteAfterUtc = DateTime.UtcNow.AddHours(-1)
        };
        database.AppContainers.Add(entry);

        // Override: return false (failure) for this specific entry
        var containerDeletion = new Mock<IContainerDeletionService>();
        containerDeletion.Setup(s => s.DeleteContainer(It.IsAny<AppContainerEntry>(), It.IsAny<string?>()))
            .Returns(false);

        using var service = CreateStartedService(database, containerDeletion.Object);
        service.ProcessExpiredContainers();

        // Entry preserved when delete fails (retry on next tick)
        Assert.Single(database.AppContainers);
        _databaseService.Verify(s => s.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void ProcessExpiredContainers_NonEphemeral_NotTouched()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry { Name = "ram_permanent", IsEphemeral = false });

        using var service = CreateStartedService(database);
        service.ProcessExpiredContainers();

        Assert.Single(database.AppContainers);
        _containerDeletion.Verify(s => s.DeleteContainer(It.IsAny<AppContainerEntry>(), It.IsAny<string?>()), Times.Never);
        _databaseService.Verify(s => s.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void ProcessExpiredContainers_PassesContainerSidToDeleteContainer()
    {
        var containerSid = "S-1-15-2-1234567890";
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry
        {
            Name = "ram_expired",
            IsEphemeral = true,
            DeleteAfterUtc = DateTime.UtcNow.AddHours(-1)
        });
        _containerService.Setup(s => s.GetSid("ram_expired")).Returns(containerSid);

        using var service = CreateStartedService(database);
        service.ProcessExpiredContainers();

        _containerDeletion.Verify(s => s.DeleteContainer(It.IsAny<AppContainerEntry>(), containerSid), Times.Once);
    }

    // --- ProcessExpiredAtStartup ---

    [Fact]
    public void ProcessExpiredAtStartup_RemovesExpiredContainersAndReferencingApps()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry
        {
            Name = "ram_expired",
            IsEphemeral = true,
            DeleteAfterUtc = DateTime.UtcNow.AddHours(-1)
        });
        database.Apps.Add(new AppEntry { Name = "App", AppContainerName = "ram_expired" });

        _containerDeletion.Setup(s => s.DeleteContainer(It.IsAny<AppContainerEntry>(), It.IsAny<string?>()))
            .Returns((AppContainerEntry entry, string? _) =>
            {
                database.AppContainers.Remove(entry);
                database.Apps.RemoveAll(a => a.AppContainerName == entry.Name);
                return true;
            });

        var changed = EphemeralContainerService.ProcessExpiredAtStartup(
            database, _containerService.Object, _containerDeletion.Object, _log.Object);

        Assert.True(changed);
        Assert.Empty(database.AppContainers);
        Assert.Empty(database.Apps);
        _containerDeletion.Verify(s => s.DeleteContainer(It.Is<AppContainerEntry>(e => e.Name == "ram_expired"), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public void ProcessExpiredAtStartup_RemovesOrphanedEntries()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry
        {
            Name = "ram_orphan",
            IsEphemeral = true
            // DeleteAfterUtc = null → orphaned
        });

        _containerDeletion.Setup(s => s.DeleteContainer(It.IsAny<AppContainerEntry>(), It.IsAny<string?>()))
            .Returns((AppContainerEntry entry, string? _) =>
            {
                database.AppContainers.Remove(entry);
                return true;
            });

        var changed = EphemeralContainerService.ProcessExpiredAtStartup(
            database, _containerService.Object, _containerDeletion.Object, _log.Object);

        Assert.True(changed);
        Assert.Empty(database.AppContainers);
        _containerDeletion.Verify(s => s.DeleteContainer(It.Is<AppContainerEntry>(e => e.Name == "ram_orphan"), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public void ProcessExpiredAtStartup_EmptyList_ReturnsFalse()
    {
        var database = new AppDatabase();

        var changed = EphemeralContainerService.ProcessExpiredAtStartup(
            database, _containerService.Object, _containerDeletion.Object, _log.Object);

        Assert.False(changed);
    }

    [Fact]
    public void ProcessExpiredAtStartup_NonExpiredEntry_NotTouched()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry
        {
            Name = "ram_future",
            IsEphemeral = true,
            DeleteAfterUtc = DateTime.UtcNow.AddHours(12) // not yet expired
        });

        var changed = EphemeralContainerService.ProcessExpiredAtStartup(
            database, _containerService.Object, _containerDeletion.Object, _log.Object);

        Assert.False(changed);
        Assert.Single(database.AppContainers);
    }

    // --- ContainersChanged event ---

    [Fact]
    public void ProcessExpiredContainers_WhenEntriesRemoved_FiresContainersChanged()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry { Name = "ram_orphan", IsEphemeral = true });

        using var service = CreateStartedService(database);
        var eventFired = false;
        service.ContainersChanged += () => eventFired = true;

        service.ProcessExpiredContainers();

        Assert.True(eventFired);
    }

    [Fact]
    public void ProcessExpiredContainers_WhenNothingChanged_DoesNotFireContainersChanged()
    {
        // No ephemeral entries → nothing to process
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry { Name = "ram_permanent", IsEphemeral = false });

        using var service = CreateStartedService(database);
        var eventFired = false;
        service.ContainersChanged += () => eventFired = true;

        service.ProcessExpiredContainers();

        Assert.False(eventFired);
    }

    [Fact]
    public void ProcessExpiredAtStartup_DeleteFailure_EntryKept_ReturnsFalse()
    {
        var database = new AppDatabase();
        database.AppContainers.Add(new AppContainerEntry
        {
            Name = "ram_fail",
            IsEphemeral = true,
            DeleteAfterUtc = DateTime.UtcNow.AddHours(-1)
        });

        _containerDeletion.Setup(s => s.DeleteContainer(It.IsAny<AppContainerEntry>(), It.IsAny<string?>()))
            .Returns(false);

        var changed = EphemeralContainerService.ProcessExpiredAtStartup(
            database, _containerService.Object, _containerDeletion.Object, _log.Object);

        // Deletion failed → entry preserved, changed=false
        Assert.False(changed);
        Assert.Single(database.AppContainers);
    }
}