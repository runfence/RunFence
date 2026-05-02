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

    private static HandlerMappingMutationHandler MakeHandler(AppDatabase db)
    {
        var svc = new Mock<IHandlerMappingService>();
        svc.Setup(s => s.GetAllHandlerMappings(db))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>(StringComparer.OrdinalIgnoreCase));
        var autoSet = new Mock<IAssociationAutoSetService>();
        return new HandlerMappingMutationHandler(svc.Object, autoSet.Object, new LambdaDatabaseProvider(() => db), new Mock<IHandlerMappingNotifier>().Object);
    }

    [Fact]
    public void Initialize_WhenHandlerMutates_CallsSync()
    {
        // Arrange
        var (syncService, _, registrationService, autoSetService, db) = Make();
        var handler = MakeHandler(db);
        syncService.Initialize(handler);

        // Act — trigger a mutation to fire Changed
        handler.RemoveDirectHandler(new DirectHandlerRowTag(".nonexistent"));

        // Assert — Sync was triggered via Changed event
        registrationService.Verify(r => r.Sync(It.IsAny<Dictionary<string, HandlerMappingEntry>>(), It.IsAny<List<AppEntry>>()), Times.Once);
        autoSetService.Verify(a => a.AutoSetForAllUsers(), Times.Once);
    }

    [Fact]
    public void Initialize_ReplacingPreviousHandler_UnsubscribesOldAndSubscribesToNew()
    {
        // Arrange
        var (syncService, _, registrationService, autoSetService, db) = Make();
        var firstHandler = MakeHandler(db);
        var secondHandler = MakeHandler(db);

        syncService.Initialize(firstHandler);
        syncService.Initialize(secondHandler);

        // Act — mutate first handler (should not trigger sync since unsubscribed)
        firstHandler.RemoveDirectHandler(new DirectHandlerRowTag(".orphan"));

        // Assert — still only zero from the first handler (unsubscribed)
        registrationService.Verify(r => r.Sync(It.IsAny<Dictionary<string, HandlerMappingEntry>>(), It.IsAny<List<AppEntry>>()), Times.Never);

        // Act — mutate second handler (should trigger sync)
        secondHandler.RemoveDirectHandler(new DirectHandlerRowTag(".active"));

        // Assert — sync called once from second handler
        registrationService.Verify(r => r.Sync(It.IsAny<Dictionary<string, HandlerMappingEntry>>(), It.IsAny<List<AppEntry>>()), Times.Once);
        autoSetService.Verify(a => a.AutoSetForAllUsers(), Times.Once);
    }

    [Fact]
    public void Sync_CallsRegistrationServiceAndAutoSet()
    {
        // Arrange
        var (syncService, _, registrationService, autoSetService, _) = Make();

        // Act
        syncService.Sync();

        // Assert
        registrationService.Verify(r => r.Sync(It.IsAny<Dictionary<string, HandlerMappingEntry>>(), It.IsAny<List<AppEntry>>()), Times.Once);
        autoSetService.Verify(a => a.AutoSetForAllUsers(), Times.Once);
    }
}
