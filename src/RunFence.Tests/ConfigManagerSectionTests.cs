using System.Text.Json;
using Moq;
using RunFence.Account;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Core.Infrastructure;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.Persistence.UI;
using RunFence.Persistence.UI.Forms;
using RunFence.Tests.Helpers;
using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class ConfigManagerSectionTests : IDisposable
{
    private readonly List<SessionContext> _sessions = [];

    public void Dispose()
    {
        foreach (var session in _sessions)
            session.Dispose();
    }

    [Fact]
    public void RegisterContextHelp_RegistersWorkflowTargets()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var section = CreateSection();
            using var host = new ContextHelpForm();

            section.RegisterContextHelp(host);

            var group = FindControls<GroupBox>(section).Single();
            var toolStrip = FindControls<ToolStrip>(section).Single();
            var listBox = FindControls<ListBox>(section).Single();

            Assert.False(host.TryGetContextHelp(group, out _));
            Assert.False(host.TryGetContextHelp(toolStrip, out _));
            Assert.False(host.TryGetContextHelp(listBox, out _));
            Assert.Empty(host.GetExplicitContextHelpToolStripDropDowns());
        });
    }

    [Fact]
    public void OnNewConfigClick_AclRestrictionFailsAndUserDeclines_DoesNotLoadUnrestrictedConfig()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tempDir = new TempDirectory("RunFence_ConfigManagerSection");
            var configPath = Path.Combine(tempDir.Path, "new-config.rfn");
            var appConfigService = new Mock<IAppConfigService>();
            var aclPermission = new Mock<IAclPermissionService>();
            var messageBoxService = new Mock<IMessageBoxService>();

            aclPermission.Setup(s => s.RestrictToAdmins(configPath))
                .Throws(new UnauthorizedAccessException("Access denied"));
            messageBoxService.SetupSequence(s => s.Show(
                    It.IsAny<string>(),
                    "Set Permissions",
                    MessageBoxButtons.YesNo,
                    It.IsAny<MessageBoxIcon>()))
                .Returns(DialogResult.Yes)
                .Returns(DialogResult.No);

            using var section = CreateSection(
                appConfigService: appConfigService.Object,
                aclPermission: aclPermission.Object,
                messageBoxService: messageBoxService.Object,
                filePicker: new StubFilePicker { SavePath = configPath });

            var loadRequested = false;
            section.ConfigLoadRequested += _ => loadRequested = true;

            FindToolbarButton(section, "New config...").PerformClick();

            Assert.False(loadRequested);
            appConfigService.Verify(
                s => s.CreateEmptyConfig(configPath, It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()),
                Times.Once);
        });
    }

    [Fact]
    public void OnNewConfigClick_AclRestrictionFailsAndUserConfirms_LoadsUnrestrictedConfig()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tempDir = new TempDirectory("RunFence_ConfigManagerSection");
            var configPath = Path.Combine(tempDir.Path, "new-config.rfn");
            var appConfigService = new Mock<IAppConfigService>();
            var aclPermission = new Mock<IAclPermissionService>();
            var messageBoxService = new Mock<IMessageBoxService>();

            aclPermission.Setup(s => s.RestrictToAdmins(configPath))
                .Throws(new UnauthorizedAccessException("Access denied"));
            messageBoxService.SetupSequence(s => s.Show(
                    It.IsAny<string>(),
                    "Set Permissions",
                    MessageBoxButtons.YesNo,
                    It.IsAny<MessageBoxIcon>()))
                .Returns(DialogResult.Yes)
                .Returns(DialogResult.Yes);

            using var section = CreateSection(
                appConfigService: appConfigService.Object,
                aclPermission: aclPermission.Object,
                messageBoxService: messageBoxService.Object,
                filePicker: new StubFilePicker { SavePath = configPath });

            string? loadedPath = null;
            section.ConfigLoadRequested += path => loadedPath = path;

            FindToolbarButton(section, "New config...").PerformClick();

            Assert.Equal(configPath, loadedPath);
        });
    }

    [Fact]
    public void ImportMainConfig_PublishesDataChanged_RefreshesConfigList_AndShowsWarnings()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tempDir = new TempDirectory("RunFence_ConfigManagerSection_MainImport");
            var importPath = Path.Combine(tempDir.Path, "import.json");
            WriteImportDatabase(importPath, new AppDatabase
            {
                Apps =
                [
                    new AppEntry { Id = "app-1", Name = "App", AppContainerName = "missing-container" }
                ]
            });

            var appConfigService = new Mock<IAppConfigService>();
            appConfigService.SetupSequence(s => s.GetLoadedConfigPaths())
                .Returns(Array.Empty<string>())
                .Returns(Array.Empty<string>());

            var messageBoxService = new Mock<IMessageBoxService>();
            messageBoxService
                .Setup(s => s.Show(
                    It.Is<string>(text => text.Contains("overwrite the current configuration", StringComparison.Ordinal)),
                    "Confirm Import",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning))
                .Returns(DialogResult.Yes);
            messageBoxService
                .Setup(s => s.Show(
                    It.Is<string>(text => text.Contains("Main config imported with warnings:", StringComparison.Ordinal) &&
                                          text.Contains("missing-container", StringComparison.Ordinal)),
                    "Import Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning))
                .Returns(DialogResult.OK);

            using var section = CreateSection(
                appConfigService: appConfigService.Object,
                messageBoxService: messageBoxService.Object,
                filePicker: new StubFilePicker { OpenPath = importPath });
            section.RefreshConfigList();

            var dataChangedCount = 0;
            section.DataChanged += () => dataChangedCount++;

            FindToolbarButton(section, "Import JSON into selected config...").PerformClick();

            Assert.Equal(1, dataChangedCount);
            appConfigService.Verify(s => s.GetLoadedConfigPaths(), Times.Exactly(2));
            messageBoxService.Verify(
                service => service.Show(
                    It.Is<string>(text => text.Contains(
                        "This will import plaintext config data and overwrite the current configuration.",
                        StringComparison.Ordinal)),
                    "Confirm Import",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning),
                Times.Once);
            messageBoxService.Verify(
                service => service.Show(
                    It.Is<string>(text => text.Contains("Main config imported with warnings:", StringComparison.Ordinal) &&
                                          text.Contains("references container 'missing-container' which is missing from the imported config.", StringComparison.Ordinal)),
                    "Import Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning),
                Times.Once);
            messageBoxService.Verify(
                service => service.Show(
                    It.IsAny<string>(),
                    "Import Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning),
                Times.Once);
        });
    }

    [Fact]
    public void ImportAdditionalConfig_Failure_ShowsPreviousErrorShape()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tempDir = new TempDirectory("RunFence_ConfigManagerSection_AdditionalImport");
            var targetConfigPath = Path.Combine(tempDir.Path, "extra.rfn");
            File.WriteAllText(targetConfigPath, "existing");
            var missingImportPath = Path.Combine(tempDir.Path, "missing.json");

            var appConfigService = new Mock<IAppConfigService>();
            appConfigService.Setup(service => service.GetLoadedConfigPaths())
                .Returns([targetConfigPath]);

            var messageBoxService = new Mock<IMessageBoxService>();
            messageBoxService
                .Setup(service => service.Show(
                    It.Is<string>(text => text.Contains("overwrite the current configuration", StringComparison.Ordinal)),
                    "Confirm Import",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning))
                .Returns(DialogResult.Yes);
            messageBoxService
                .Setup(service => service.Show(
                    It.Is<string>(text => text.StartsWith("Import failed: Additional config import failed (ValidationFailed):", StringComparison.Ordinal)),
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error))
                .Returns(DialogResult.OK);

            var log = new Mock<ILoggingService>();
            var loadService = new Mock<IAdditionalConfigLoadService>();
            loadService.Setup(service => service.UnloadApps(targetConfigPath)).Returns(true);
            loadService.Setup(service => service.LoadApps(targetConfigPath)).Returns(new LoadAppsResult(true, null));

            using var section = CreateSection(
                appConfigService: appConfigService.Object,
                log: log.Object,
                messageBoxService: messageBoxService.Object,
                filePicker: new StubFilePicker { OpenPath = missingImportPath },
                additionalImportController: new AdditionalConfigImportController(
                    new AdditionalConfigImportCoordinator(
                        appConfigService.Object,
                        loadService.Object,
                        CreateImportHandler(CreateSessionProvider()),
                        log.Object)));
            section.RefreshConfigList();
            FindControls<ListBox>(section).Single().SelectedIndex = 1;

            FindToolbarButton(section, "Import JSON into selected config...").PerformClick();

            messageBoxService.Verify(
                service => service.Show(
                    It.Is<string>(text => text.Contains("overwrite the current configuration", StringComparison.Ordinal)),
                    "Confirm Import",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning),
                Times.Once);
            messageBoxService.Verify(
                service => service.Show(
                    It.Is<string>(text => text.StartsWith("Import failed: Additional config import failed (ValidationFailed):", StringComparison.Ordinal)),
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error),
                Times.Once);
            messageBoxService.Verify(
                service => service.Show(
                    It.IsAny<string>(),
                    "Import Complete",
                    It.IsAny<MessageBoxButtons>(),
                    It.IsAny<MessageBoxIcon>()),
                Times.Never);
            messageBoxService.Verify(
                service => service.Show(
                    It.IsAny<string>(),
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error),
                Times.Once);
        });
    }

    private ConfigManagerSection CreateSection(
        IAppConfigService? appConfigService = null,
        IAclPermissionService? aclPermission = null,
        ILoggingService? log = null,
        IMessageBoxService? messageBoxService = null,
        IConfigImportExportFilePicker? filePicker = null,
        ISessionProvider? sessionProvider = null,
        ConfigExportController? exportController = null,
        MainConfigImportController? mainImportController = null,
        AdditionalConfigImportController? additionalImportController = null)
    {
        sessionProvider ??= CreateSessionProvider();
        return new ConfigManagerSection(
            appConfigService ?? Mock.Of<IAppConfigService>(),
            aclPermission ?? Mock.Of<IAclPermissionService>(),
            log ?? Mock.Of<ILoggingService>(),
            sessionProvider,
            filePicker ?? new StubFilePicker(),
            exportController ?? new ConfigExportController(
                Mock.Of<IAppConfigService>(),
                Mock.Of<IAppFilter>(),
                sessionProvider,
                Mock.Of<IFileContentService>(),
                Mock.Of<ILoggingService>()),
            mainImportController ?? new MainConfigImportController(
                sessionProvider,
                CreateSidResolutionService(sessionProvider),
                CreateImportHandler(sessionProvider),
                CreateMappingService().Object,
                Mock.Of<IHandlerSyncService>(),
                Mock.Of<ILoggingService>()),
            additionalImportController ?? new AdditionalConfigImportController(
                new AdditionalConfigImportCoordinator(
                    Mock.Of<IAppConfigService>(),
                    Mock.Of<IAdditionalConfigLoadService>(),
                    CreateImportHandler(sessionProvider),
                    Mock.Of<ILoggingService>())),
            messageBoxService ?? Mock.Of<IMessageBoxService>());
    }

    private ISessionProvider CreateSessionProvider()
    {
        var session = new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
        _sessions.Add(session);

        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(session);
        return sessionProvider.Object;
    }

    private static IAccountSidResolutionService CreateSidResolutionService(ISessionProvider sessionProvider)
    {
        var sidResolutionService = new Mock<IAccountSidResolutionService>();
        sidResolutionService.Setup(service => service.ResolveSidsAsync(
                sessionProvider.GetSession().CredentialStore,
                sessionProvider.GetSession().Database.SidNames))
            .ReturnsAsync(new Dictionary<string, string?>());
        return sidResolutionService.Object;
    }

    private static ConfigImportHandler CreateImportHandler(ISessionProvider sessionProvider)
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

    private static ToolStripButton FindToolbarButton(Control root, string toolTipText)
        => FindControls<ToolStrip>(root)
            .SelectMany(toolStrip => toolStrip.Items.OfType<ToolStripButton>())
            .Single(button => string.Equals(button.ToolTipText, toolTipText, StringComparison.Ordinal));

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                yield return match;

            foreach (var nested in FindControls<T>(child))
                yield return nested;
        }
    }

    private sealed class StubFilePicker : IConfigImportExportFilePicker
    {
        public string? SavePath { get; init; }

        public string? OpenPath { get; init; }

        public string? SelectSavePath(string filter, string title) => SavePath;

        public string? SelectOpenPath(string filter, string title) => OpenPath;
    }
}
