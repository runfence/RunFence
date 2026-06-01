using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Persistence.UI;
using RunFence.Startup;
using Xunit;

namespace RunFence.Tests;

public sealed class ConfigSaveOrchestratorTests : IDisposable
{
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);
    private readonly AppDatabase _database = new();
    private readonly CredentialStore _credentialStore = new() { ArgonSalt = new byte[32] };

    public void Dispose() => _pinKey.Dispose();

    [Fact]
    public async Task SaveMainConfig_WorkerThread_MarshalsEncryptedSaveToUiThread()
    {
        using var uiInvoker = new DedicatedThreadUiInvoker();
        var databaseService = new Mock<IDatabaseService>();
        var orchestrator = CreateOrchestrator(
            uiInvoker,
            databaseService: databaseService);

        var saveThreadId = 0;
        databaseService
            .Setup(service => service.SaveConfig(_database, It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
            .Callback(() => saveThreadId = Environment.CurrentManagedThreadId);

        await Task.Run(orchestrator.SaveMainConfig);

        Assert.Equal(uiInvoker.ThreadId, saveThreadId);
    }

    [Fact]
    public async Task SaveAdditionalConfig_WorkerThread_MarshalsEncryptedSaveToUiThread()
    {
        using var uiInvoker = new DedicatedThreadUiInvoker();
        var databaseService = new Mock<IDatabaseService>();
        var configPath = Path.GetFullPath(@"C:\configs\extra.rfn");

        var orchestrator = CreateOrchestrator(
            uiInvoker,
            databaseService: databaseService);

        var saveThreadId = 0;
        databaseService
            .Setup(service => service.SaveAppConfig(
                It.IsAny<AppConfig>(),
                configPath,
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()))
            .Callback(() => saveThreadId = Environment.CurrentManagedThreadId);

        await Task.Run(() => orchestrator.SaveAdditionalConfig(configPath, []));

        Assert.Equal(uiInvoker.ThreadId, saveThreadId);
    }

    [Fact]
    public async Task SaveConfigAfterEnforcement_WorkerThread_MarshalsEncryptedSaveToUiThread()
    {
        using var uiInvoker = new DedicatedThreadUiInvoker();
        var appConfigService = new Mock<IAppConfigService>();
        var orchestrator = CreateOrchestrator(
            uiInvoker,
            appConfigService: appConfigService);

        var saveThreadId = 0;
        appConfigService
            .Setup(service => service.SaveAllConfigs(_database, It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
            .Callback(() => saveThreadId = Environment.CurrentManagedThreadId);

        await Task.Run(() => orchestrator.SaveConfigAfterEnforcement(_database));

        Assert.Equal(uiInvoker.ThreadId, saveThreadId);
    }

    private ConfigSaveOrchestrator CreateOrchestrator(
        IUiThreadInvoker uiThreadInvoker,
        Mock<IDatabaseService>? databaseService = null,
        Mock<IAppConfigService>? appConfigService = null)
    {
        var sessionProvider = new SessionProvider();
        sessionProvider.SetSession(new SessionContext
{
            Database = _database,
            CredentialStore = _credentialStore,
        }.WithClonedPinDerivedKey(_pinKey));
        var effectiveDatabaseService = databaseService ?? new Mock<IDatabaseService>();
        var effectiveAppConfigService = appConfigService ?? new Mock<IAppConfigService>();

        return new ConfigSaveOrchestrator(
            sessionProvider,
            () => uiThreadInvoker,
            effectiveDatabaseService.Object,
            effectiveAppConfigService.Object,
            Mock.Of<IHandlerMappingService>());
    }
}
