using System.Text.Json;
using Moq;
using RunFence.Account;
using RunFence.Acl;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.Persistence.UI;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class MainConfigImportControllerTests : IDisposable
{
    private readonly List<SessionContext> _sessions = [];

    public void Dispose()
    {
        foreach (var session in _sessions)
            session.Dispose();
    }

    [Fact]
    public async Task ImportAsync_NoRemovedKeys_PublishesBeforeParameterlessSync()
    {
        var database = new AppDatabase();
        database.Settings.HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["http"] = new("app-1")
        };
        using var tempDir = new TempDirectory("RunFence_MainConfigImportController_NoRemoved");
        var importPath = Path.Combine(tempDir.Path, "import.json");
        WriteImportDatabase(importPath, new AppDatabase
        {
            Settings =
            {
                HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    ["http"] = new("app-1")
                }
            }
        });

        var controller = CreateController(database);
        var callOrder = new List<string>();
        controller.HandlerSyncService.Setup(service => service.Sync())
            .Callback(() => callOrder.Add("sync"));

        var result = await controller.Controller.ImportAsync(
            importPath,
            () => callOrder.Add("publish"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(["publish", "sync"], callOrder);
        controller.HandlerSyncService.Verify(service => service.Sync(), Times.Once);
        controller.HandlerSyncService.Verify(service => service.Sync(It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_RemovedOrdinaryKey_PublishesBeforeRemovedKeySync()
    {
        var database = new AppDatabase();
        database.Settings.HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["http"] = new("app-1"),
            ["https"] = new("app-1")
        };
        using var tempDir = new TempDirectory("RunFence_MainConfigImportController_RemovedOrdinary");
        var importPath = Path.Combine(tempDir.Path, "import.json");
        WriteImportDatabase(importPath, new AppDatabase
        {
            Settings =
            {
                HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    ["https"] = new("app-1")
                }
            }
        });

        var controller = CreateController(database);
        IReadOnlyCollection<string>? capturedRemovedKeys = null;
        var callOrder = new List<string>();
        controller.HandlerSyncService
            .Setup(service => service.Sync(It.IsAny<IReadOnlyCollection<string>>()))
            .Callback<IReadOnlyCollection<string>?>(keys =>
            {
                capturedRemovedKeys = keys;
                callOrder.Add("sync");
            });

        await controller.Controller.ImportAsync(
            importPath,
            () => callOrder.Add("publish"),
            CancellationToken.None);

        Assert.Equal(["publish", "sync"], callOrder);
        Assert.Equal(["http"], capturedRemovedKeys);
    }

    [Fact]
    public async Task ImportAsync_RemovedDirectKey_IncludesDirectKeyInRemovedSet()
    {
        var database = new AppDatabase();
        database.Settings.DirectHandlerMappings = new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = new DirectHandlerEntry { Command = @"""C:\Windows\notepad.exe"" ""%1""" }
        };
        using var tempDir = new TempDirectory("RunFence_MainConfigImportController_RemovedDirect");
        var importPath = Path.Combine(tempDir.Path, "import.json");
        WriteImportDatabase(importPath, new AppDatabase());

        var controller = CreateController(database);
        IReadOnlyCollection<string>? capturedRemovedKeys = null;
        controller.HandlerSyncService
            .Setup(service => service.Sync(It.IsAny<IReadOnlyCollection<string>>()))
            .Callback<IReadOnlyCollection<string>?>(keys => capturedRemovedKeys = keys);

        await controller.Controller.ImportAsync(
            importPath,
            () => { },
            CancellationToken.None);

        Assert.Equal([".txt"], capturedRemovedKeys);
    }

    [Fact]
    public async Task ImportAsync_HandlerSyncFailure_AppendsWarning()
    {
        var database = new AppDatabase();
        using var tempDir = new TempDirectory("RunFence_MainConfigImportController_HandlerSyncFailure");
        var importPath = Path.Combine(tempDir.Path, "import.json");
        WriteImportDatabase(importPath, new AppDatabase());

        var controller = CreateController(database);
        controller.HandlerSyncService.Setup(service => service.Sync())
            .Throws(new InvalidOperationException("sync exploded"));

        var result = await controller.Controller.ImportAsync(
            importPath,
            () => { },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains(result.Warnings, warning => warning.Contains("sync exploded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportAsync_IncludesValidationWarnings_FromImportState()
    {
        var database = new AppDatabase();
        using var tempDir = new TempDirectory("RunFence_MainConfigImportController_ValidationWarnings");
        var importPath = Path.Combine(tempDir.Path, "import.json");
        WriteImportDatabase(importPath, new AppDatabase
        {
            Apps =
            [
                new AppEntry { Id = "app-1", Name = "Imported", AppContainerName = "missing-container" }
            ]
        });

        var controller = CreateController(database);
        var result = await controller.Controller.ImportAsync(
            importPath,
            () => { },
            CancellationToken.None);

        Assert.Contains(
            "App 'Imported' references container 'missing-container' which is missing from the imported config.",
            result.Warnings);
    }

    [Fact]
    public void HandlerSyncHelper_RemovedKeys_RestoresRemovedOrdinaryAndDirectKeys_ThenPerformsFullSync()
    {
        var database = new AppDatabase
        {
            Apps = [new AppEntry { Id = "app-1", Name = "App" }]
        };
        database.Settings.HandlerMappings = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["https"] = new("app-1")
        };
        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(CreateSession(database));

        var handlerRegistrationService = new Mock<IAppHandlerRegistrationService>();
        var autoSetService = new Mock<IAssociationAutoSetService>();
        var helper = new HandlerSyncHelper(
            sessionProvider.Object,
            handlerRegistrationService.Object,
            CreateMappingService().Object,
            autoSetService.Object);

        helper.Sync(["http", ".txt"]);

        autoSetService.Verify(service => service.RestoreKeyForAllUsers("http"), Times.Once);
        autoSetService.Verify(service => service.RestoreKeyForAllUsers(".txt"), Times.Once);
        handlerRegistrationService.Verify(
            service => service.Sync(
                It.Is<Dictionary<string, HandlerMappingEntry>>(mappings => mappings.Keys.SequenceEqual(new[] { "https" })),
                database.Apps),
            Times.Once);
        autoSetService.Verify(service => service.AutoSetForAllUsers(), Times.Once);
    }

    private ControllerHarness CreateController(AppDatabase database)
    {
        var session = CreateSession(database);
        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(session);

        var sidResolutionService = new Mock<IAccountSidResolutionService>();
        sidResolutionService
            .Setup(service => service.ResolveSidsAsync(session.CredentialStore, session.Database.SidNames))
            .ReturnsAsync(new Dictionary<string, string?>());

        var handlerSyncService = new Mock<IHandlerSyncService>();
        var controller = new MainConfigImportController(
            sessionProvider.Object,
            sidResolutionService.Object,
            CreateImportHandler(sessionProvider.Object),
            CreateMappingService().Object,
            handlerSyncService.Object,
            Mock.Of<ILoggingService>());

        return new ControllerHarness(controller, handlerSyncService);
    }

    private ConfigImportHandler CreateImportHandler(ISessionProvider sessionProvider)
    {
        var appConfigService = new Mock<IAppConfigService>();
        appConfigService.Setup(service => service.HasLoadedConfigs).Returns(false);
        appConfigService.Setup(service => service.GetConfigPath(It.IsAny<string>())).Returns((string?)null);
        appConfigService.Setup(service => service.GetLoadedConfigPaths()).Returns(Array.Empty<string>());

        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(service => service.IsLicensed).Returns(true);
        licenseService.Setup(service => service.GetRestrictionMessage(It.IsAny<EvaluationFeature>(), It.IsAny<int>()))
            .Returns((string?)null);

        var handlerMappingService = CreateMappingService();
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        ownershipProjection.CaptureMainOwnershipBaseline(sessionProvider.GetSession().Database);
        var preservationCollector = new MainConfigImportPreservationCollector(ownershipProjection);
        var evaluationValidator = new MainConfigImportEvaluationValidator(licenseService.Object, appConfigService.Object);
        var repairService = new MainConfigImportRepairService(
            appConfigService.Object,
            handlerMappingService.Object,
            Mock.Of<ILoggingService>(),
            Mock.Of<IAppEntryIdGenerator>(),
            new AppIdValidator());
        var applyService = new MainConfigImportApplyService(
            appConfigService.Object,
            repairService,
            ownershipProjection,
            Mock.Of<IGrantInspectionService>());

        return new ConfigImportHandler(
            appConfigService.Object,
            sessionProvider,
            Mock.Of<ILoggingService>(),
            new ConfigImportFileParser(),
            preservationCollector,
            evaluationValidator,
            repairService,
            applyService);
    }

    private SessionContext CreateSession(AppDatabase database)
    {
        var session = new SessionContext
        {
            Database = database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
        _sessions.Add(session);
        return session;
    }

    private static Mock<IHandlerMappingService> CreateMappingService()
    {
        var service = new Mock<IHandlerMappingService>();
        service.Setup(mapping => mapping.GetEffectiveHandlerMappings(It.IsAny<AppDatabase>()))
            .Returns<AppDatabase>(database => database.Settings.HandlerMappings != null
                ? new Dictionary<string, HandlerMappingEntry>(database.Settings.HandlerMappings, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase));
        service.Setup(mapping => mapping.GetEffectiveDirectHandlerMappings(It.IsAny<AppDatabase>()))
            .Returns<AppDatabase>(database => database.Settings.DirectHandlerMappings != null
                ? new Dictionary<string, DirectHandlerEntry>(database.Settings.DirectHandlerMappings, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase));
        return service;
    }

    private static void WriteImportDatabase(string path, AppDatabase database)
    {
        var json = JsonSerializer.Serialize(database, JsonDefaults.Options);
        File.WriteAllText(path, json);
    }

    private sealed record ControllerHarness(
        MainConfigImportController Controller,
        Mock<IHandlerSyncService> HandlerSyncService);
}
