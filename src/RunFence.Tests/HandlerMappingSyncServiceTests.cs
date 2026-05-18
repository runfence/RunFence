using Moq;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Core.Models;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class HandlerMappingSyncServiceTests
{
    private static (HandlerMappingSyncService syncService,
        Mock<IHandlerMappingService> mappingService,
        Mock<IAppHandlerRegistrationService> registrationService,
        Mock<IAssociationAutoSetService> autoSetService,
        AppDatabase db) Make()
    {
        var mappingService = new Mock<IHandlerMappingService>();
        var registrationService = new Mock<IAppHandlerRegistrationService>();
        var autoSetService = new Mock<IAssociationAutoSetService>();
        var db = new AppDatabase();
        var dbProvider = new LambdaDatabaseProvider(() => db);

        mappingService.Setup(s => s.GetEffectiveHandlerMappings(db))
            .Returns(new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase));

        var syncService = new HandlerMappingSyncService(
            mappingService.Object, registrationService.Object, autoSetService.Object, dbProvider);

        return (syncService, mappingService, registrationService, autoSetService, db);
    }

    [Fact]
    public void Sync_CallsRegistrationServiceAndAutoSet()
    {
        var (syncService, _, registrationService, autoSetService, _) = Make();

        var result = syncService.Sync();

        Assert.True(result.Succeeded);
        Assert.Null(result.WarningMessage);
        registrationService.Verify(r => r.Sync(It.IsAny<Dictionary<string, HandlerMappingEntry>>(), It.IsAny<List<AppEntry>>()), Times.Once);
        autoSetService.Verify(a => a.AutoSetForAllUsers(), Times.Once);
    }

    [Fact]
    public void Sync_WhenRegistrationThrows_ReturnsWarningInsteadOfThrowing()
    {
        var (syncService, _, registrationService, autoSetService, _) = Make();
        registrationService.Setup(r => r.Sync(It.IsAny<Dictionary<string, HandlerMappingEntry>>(), It.IsAny<List<AppEntry>>()))
            .Throws(new InvalidOperationException("sync failed"));

        var result = syncService.Sync();

        Assert.False(result.Succeeded);
        Assert.Equal("sync failed", result.WarningMessage);
        autoSetService.Verify(a => a.AutoSetForAllUsers(), Times.Never);
    }

    [Fact]
    public void Sync_WithRestoreKeys_RestoresDistinctKeysBeforeSync()
    {
        var (syncService, _, registrationService, autoSetService, _) = Make();

        var result = syncService.Sync([".txt", ".TXT", "http"]);

        Assert.True(result.Succeeded);
        autoSetService.Verify(a => a.RestoreKeyForAllUsers(".txt"), Times.Once);
        autoSetService.Verify(a => a.RestoreKeyForAllUsers("http"), Times.Once);
        registrationService.Verify(r => r.Sync(It.IsAny<Dictionary<string, HandlerMappingEntry>>(), It.IsAny<List<AppEntry>>()), Times.Once);
    }
}
