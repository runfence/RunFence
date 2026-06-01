using Moq;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Persistence;

namespace RunFence.Tests.Helpers;

internal sealed class AppConfigTestContext
{
    public Mock<ILoggingService> LoggingService { get; } = new();
    public Mock<IDatabaseService> DatabaseService { get; } = new();
    public GrantIntentOwnershipProjectionService OwnershipProjection { get; } = new();
    public AppIdValidator AppIdValidator { get; } = new();
    public AppConfigIndex Index { get; }
    public HandlerMappingService HandlerMappingService { get; }
    public TestGrantIntentStoreProvider GrantIntentStoreProvider { get; }
    public AppConfigService Service { get; }

    public AppConfigTestContext()
    {
        Index = new AppConfigIndex(OwnershipProjection, AppIdValidator);
        HandlerMappingService = new HandlerMappingService(Index);
        GrantIntentStoreProvider = new TestGrantIntentStoreProvider(
            new TestGrantIntentStore(),
            OwnershipProjection);
        Service = new AppConfigService(
            LoggingService.Object,
            Index,
            OwnershipProjection,
            () => GrantIntentStoreProvider,
            HandlerMappingService,
            DatabaseService.Object,
            new AppConfigSaveHelper(
                () => GrantIntentStoreProvider,
                HandlerMappingService,
                DatabaseService.Object),
            new AppEntryIdGenerator(),
            AppIdValidator);
    }

    public void AddLoadedConfig(string configPath)
    {
        var normalizedPath = Path.GetFullPath(configPath);
        if (!Index.ContainsLoadedPath(normalizedPath))
            Index.AddLoadedPath(normalizedPath);

        GrantIntentStoreProvider.AddLoadedStore(new TestGrantIntentStore(normalizedPath));
    }
}
