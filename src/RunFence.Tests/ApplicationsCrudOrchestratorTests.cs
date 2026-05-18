using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.QuickAccess;
using RunFence.Acl.UI;
using RunFence.Acl.UI.Forms;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Launch;
using RunFence.Launching.Resolution;
using RunFence.Persistence;
using RunFence.RunAs.UI;
using RunFence.Tests.Helpers;
using RunFence.UI;
using RunFence.UI.Forms;
using System.Runtime.CompilerServices;
using Xunit;

namespace RunFence.Tests;

public class ApplicationsCrudOrchestratorTests
{
    private const string TestAccountSid = "S-1-5-21-1-2-3-1001";

    [Fact]
    public void ApplyChanges_SaveFailed_DoesNotMutateRetryStatus()
    {
        var database = new AppDatabase();
        var retryStatus = new AppEnforcementRetryStatus("existing", DateTime.UtcNow);
        var app = new AppEntry
        {
            Id = "app01",
            Name = "Edited App",
            ExePath = @"C:\app.exe",
            EnforcementRetryStatus = retryStatus
        };
        database.Apps.Add(app);

        var context = new RecordingContext(database);
        var operationService = CreateOperationService(Mock.Of<IAclService>());

        var result = operationService.ApplyChanges(
            context,
            app,
            new ShortcutTraversalCache([]),
            selectAppId: app.Id,
            targetedSave: true);

        Assert.Equal(ApplicationsCrudOperationStatus.SaveFailed, result.Status);
        Assert.Equal("save failed", result.ErrorMessage);
        Assert.NotNull(app.EnforcementRetryStatus);
        Assert.Equal("existing", app.EnforcementRetryStatus.FailureMessage);
        Assert.Equal(retryStatus.FailureMessage, app.EnforcementRetryStatus!.FailureMessage);
    }

    [Fact]
    public void OpenAddDialog_DuplicateId_IsRejectedWithoutPersistence()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var database = new AppDatabase();
            var existing = new AppEntry { Id = "app01", Name = "Existing", ExePath = @"C:\existing.exe", AccountSid = TestAccountSid, ManageShortcuts = false };
            database.Apps.Add(existing);

            var context = new OrchestratorContext(database);
            var operationService = CreateOperationService(Mock.Of<IAclService>());
            var (orchestrator, appConfigService, messageBox, licenseService) = CreateOrchestrator(
                context,
                database,
                operationService);
            orchestrator.Initialize(context);

            var duplicate = new AppEntry
            {
                Id = "app01",
                Name = "Duplicate",
                ExePath = @"C:\duplicate.exe",
                AccountSid = TestAccountSid,
                ManageShortcuts = false
            };
            context.DialogFactory = () => CreateDialog(database, appConfigService.Object, duplicate.Id);
            context.ShowModal = d =>
            {
                SubmitDialog(d, duplicate);
                Assert.True(d.HasUnsavedInMemoryMutations);
                Assert.Equal(DialogResult.None, d.DialogResult);
            };

            context.CredentialStore.Credentials.Add(new CredentialEntry { Sid = TestAccountSid });
            orchestrator.OpenAddDialog(initialAccountSid: TestAccountSid, initialExePath: duplicate.ExePath);
            Assert.Single(database.Apps);
            Assert.Empty(context.SaveCalls);
            Assert.Single(context.RefreshCalls);
            Assert.Equal(("app01", -1), context.RefreshCalls.Single());
            messageBox.Verify(
                m => m.Show(It.IsAny<string>(), "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error),
                Times.Never);
            messageBox.Verify(
                m => m.Show(
                    It.Is<string>(s => s.Contains("could not save the change")),
                    It.IsAny<string>(),
                    It.IsAny<MessageBoxButtons>(),
                    It.IsAny<MessageBoxIcon>()),
                Times.Never);
            licenseService.Verify(s => s.CanAddApp(It.IsAny<int>()), Times.Once);
        });
    }

    [Fact]
    public void OpenAddDialog_SaveFailure_RollsBackAddedApp_ByIdAndTargetsSave()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var database = new AppDatabase();
            database.Apps.Add(new AppEntry { Id = "existing", Name = "Existing", ExePath = @"C:\existing.exe", ManageShortcuts = false });

            var context = new OrchestratorContext(database) { SaveException = new IOException("save failed") };
            var operationService = CreateOperationService(new Mock<IAclService>().Object);
            var (orchestrator, appConfigService, messageBox, _) = CreateOrchestrator(
                context,
                database,
                operationService);
            orchestrator.Initialize(context);

            var added = new AppEntry
            {
                Id = "newapp",
                Name = "New App",
                ExePath = @"C:\new.exe",
                AccountSid = TestAccountSid,
                ManageShortcuts = false
            };
            context.DialogFactory = () => CreateDialog(database, appConfigService.Object, added.Id);
            context.ShowModal = d =>
            {
                SubmitDialog(d, added);
                Assert.Empty(context.RefreshCalls);
                Assert.True(d.HasUnsavedInMemoryMutations);
                Assert.Equal(DialogResult.None, d.DialogResult);
            };

            context.CredentialStore.Credentials.Add(new CredentialEntry { Sid = TestAccountSid });
            orchestrator.OpenAddDialog(initialAccountSid: TestAccountSid, initialExePath: added.ExePath);
            Assert.Single(database.Apps);
            Assert.DoesNotContain(database.Apps, a => a.Id == "newapp");
            Assert.Single(context.SaveCalls);
            var saveCall = context.SaveCalls.Single();
            Assert.Equal("newapp", saveCall.selectAppId);
            Assert.True(saveCall.targetedSave);
            Assert.Single(context.RefreshCalls);
            Assert.Equal(("newapp", -1), context.RefreshCalls.Single());
            messageBox.Verify(
                m => m.Show(
                    It.Is<string>(s => s.Contains("RunFence could not save the change")),
                    "Save Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error),
                Times.Once);
        });
    }

    [Fact]
    public void OpenEditDialog_SaveFailure_RestoresPreviousAppAndReappliesEnforcement()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var database = new AppDatabase();
            var original = new AppEntry
            {
                Id = "app01",
                Name = "Original",
                ExePath = @"C:\original.exe",
                AccountSid = TestAccountSid,
                ManageShortcuts = false,
                RestrictAcl = true,
                EnforcementRetryStatus = new AppEnforcementRetryStatus("keep", DateTime.UtcNow)
            };
            database.Apps.Add(original);

            var aclService = new Mock<IAclService>();
            aclService.Setup(s => s.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()));
            aclService.Setup(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()));
            var operationService = CreateOperationService(aclService.Object);

            var context = new OrchestratorContext(database) { SaveException = new IOException("save failed") };
            var (orchestrator, appConfigService, messageBox, _) = CreateOrchestrator(
                context,
                database,
                operationService);
            context.Grid.Columns.Add("name", "Name");
            context.Grid.Rows.Add(new DataGridViewRow { Tag = original });
            context.Grid.Rows[0].Selected = true;
            orchestrator.Initialize(context);

            var edited = new AppEntry
            {
                Id = "app01",
                Name = "Edited",
                ExePath = @"C:\edited.exe",
                AccountSid = TestAccountSid,
                ManageShortcuts = false,
                RestrictAcl = true
            };
            context.DialogFactory = () => CreateDialog(database, appConfigService.Object, edited.Id);
            context.ShowModal = d =>
            {
                SubmitDialog(d, edited);
                Assert.Empty(context.RefreshCalls);
                Assert.True(d.HasUnsavedInMemoryMutations);
                Assert.Equal(DialogResult.None, d.DialogResult);
            };

            context.CredentialStore.Credentials.Add(new CredentialEntry { Sid = TestAccountSid });
            orchestrator.EditApp(original);
            Assert.Single(database.Apps);
            Assert.Equal("Original", database.Apps.Single().Name);
            Assert.NotNull(database.Apps.Single().EnforcementRetryStatus);
            Assert.Equal("keep", database.Apps.Single().EnforcementRetryStatus!.FailureMessage);
            Assert.Single(context.SaveCalls);
            var saveCall = context.SaveCalls.Single();
            Assert.Equal("app01", saveCall.selectAppId);
            Assert.False(saveCall.targetedSave);
            Assert.Single(context.RefreshCalls);
            Assert.Equal(("app01", 0), context.RefreshCalls.Single());
            aclService.Verify(s => s.ApplyAcl(It.Is<AppEntry>(a => a.Name == "Original"), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Once);
            aclService.Verify(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()), Times.Exactly(2));
            messageBox.Verify(
                m => m.Show(
                It.Is<string>(s => s.Contains("Application save failed because RunFence could not save the change")),
                "Save Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error),
                Times.Once);
        });
    }

    [Fact]
    public void OpenAddDialog_LicenseRestriction_ShowsDialogErrorAndSkipsPersistence()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var database = new AppDatabase();
            var context = new OrchestratorContext(database);
            var operationService = CreateOperationService(Mock.Of<IAclService>());
            var (orchestrator, appConfigService, messageBox, licenseService) = CreateOrchestrator(
                context,
                database,
                operationService);
            licenseService.Setup(s => s.CanAddApp(It.IsAny<int>())).Returns(false);
            licenseService.Setup(s => s.GetRestrictionMessage(EvaluationFeature.Apps, It.IsAny<int>()))
                .Returns("license message");
            orchestrator.Initialize(context);

            var added = new AppEntry
            {
                Id = "newapp",
                Name = "New App",
                ExePath = @"C:\new.exe",
                AccountSid = TestAccountSid,
                ManageShortcuts = false
            };
            context.DialogFactory = () => CreateDialog(database, appConfigService.Object, added.Id);
            context.ShowModal = d =>
            {
                SubmitDialog(d, added);

                Assert.True(d.HasUnsavedInMemoryMutations);
                Assert.Equal(DialogResult.None, d.DialogResult);
                Assert.Equal("Failed: license message", FindControlText<Label>(d, "Failed: license message"));
            };

            context.CredentialStore.Credentials.Add(new CredentialEntry { Sid = TestAccountSid });
            orchestrator.OpenAddDialog(initialAccountSid: TestAccountSid, initialExePath: added.ExePath);

            Assert.Empty(database.Apps);
            Assert.Empty(context.SaveCalls);
            Assert.Single(context.RefreshCalls);
            Assert.Equal(("newapp", -1), context.RefreshCalls.Single());
            messageBox.Verify(
                m => m.Show(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageBoxButtons>(), It.IsAny<MessageBoxIcon>()),
                Times.Never);
        });
    }

    [Fact]
    public void OpenEditDialog_CleanupFailure_SavesAndClosesWithWarning()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var database = new AppDatabase();
            var original = new AppEntry
            {
                Id = "app01",
                Name = "Original",
                ExePath = @"C:\original.exe",
                AccountSid = TestAccountSid,
                ManageShortcuts = false,
                RestrictAcl = true,
                EnforcementRetryStatus = new AppEnforcementRetryStatus("keep", DateTime.UtcNow)
            };
            database.Apps.Add(original);

            var aclService = new Mock<IAclService>();
            aclService.Setup(s => s.RevertAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()))
                .Throws(new InvalidOperationException("revert failed"));
            aclService.Setup(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()));
            var operationService = CreateOperationService(aclService.Object);

            var context = new OrchestratorContext(database);
            var (orchestrator, appConfigService, messageBox, _) = CreateOrchestrator(
                context,
                database,
                operationService);
            context.Grid.Columns.Add("name", "Name");
            context.Grid.Rows.Add(new DataGridViewRow { Tag = original });
            context.Grid.Rows[0].Selected = true;
            orchestrator.Initialize(context);

            var edited = new AppEntry
            {
                Id = "app01",
                Name = "Edited",
                ExePath = @"C:\edited.exe",
                AccountSid = TestAccountSid,
                ManageShortcuts = false,
                RestrictAcl = true
            };
            context.DialogFactory = () => CreateDialog(database, appConfigService.Object);
            context.ShowModal = d =>
            {
                SubmitDialog(d, edited);
                Assert.False(d.HasUnsavedInMemoryMutations);
                Assert.Equal(DialogResult.OK, d.DialogResult);
            };

            context.CredentialStore.Credentials.Add(new CredentialEntry { Sid = TestAccountSid });
            orchestrator.EditApp(original);
            Assert.Single(database.Apps);
            Assert.Equal("Edited", database.Apps.Single().Name);
            Assert.Single(context.SaveCalls);
            Assert.Empty(context.RefreshCalls);
            messageBox.Verify(
                m => m.Show(
                    It.Is<string>(s => s.Contains("Application was saved, but enforcement failed and needs retry")),
                    "Saved With Warning",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning),
                Times.Once);
        });
    }

    [Fact]
    public void OpenEditDialog_SaveFailureAfterCleanupWarning_RollsBackDatabaseAndKeepsDialogOpen()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var database = new AppDatabase();
            var original = new AppEntry
            {
                Id = "app01",
                Name = "Original",
                ExePath = @"C:\original.exe",
                AccountSid = TestAccountSid,
                ManageShortcuts = false,
                RestrictAcl = true
            };
            database.Apps.Add(original);

            var aclService = new Mock<IAclService>();
            aclService.Setup(s => s.RevertAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()))
                .Throws(new InvalidOperationException("revert failed"));
            aclService.Setup(s => s.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()));
            aclService.Setup(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()));
            var operationService = CreateOperationService(aclService.Object);

            var context = new OrchestratorContext(database) { SaveException = new IOException("save failed") };
            var (orchestrator, appConfigService, messageBox, _) = CreateOrchestrator(
                context,
                database,
                operationService);
            orchestrator.Initialize(context);

            var edited = new AppEntry
            {
                Id = "app01",
                Name = "Edited",
                ExePath = @"C:\edited.exe",
                AccountSid = TestAccountSid,
                ManageShortcuts = false,
                RestrictAcl = true
            };
            context.DialogFactory = () => CreateDialog(database, appConfigService.Object, edited.Id);
            context.ShowModal = d =>
            {
                SubmitDialog(d, edited);
                Assert.True(d.HasUnsavedInMemoryMutations);
                Assert.Equal(DialogResult.None, d.DialogResult);
            };

            context.CredentialStore.Credentials.Add(new CredentialEntry { Sid = TestAccountSid });
            orchestrator.EditApp(original);

            Assert.Single(database.Apps);
            Assert.Equal("Original", database.Apps.Single().Name);
            Assert.Single(context.SaveCalls);
            Assert.Single(context.RefreshCalls);
            Assert.Equal(("app01", -1), context.RefreshCalls.Single());
            messageBox.Verify(
                m => m.Show(
                    It.Is<string>(s => s.Contains("revert failed") && s.Contains("save failed")),
                    "Save Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error),
                Times.Once);
        });
    }

    [Fact]
    public void OpenEditDialog_SaveFailure_RollsBackDatabaseButKeepsEditedDialogState()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var database = new AppDatabase();
            var original = new AppEntry
            {
                Id = "app01",
                Name = "Original",
                ExePath = @"C:\original.exe",
                AccountSid = TestAccountSid,
                ManageShortcuts = false
            };
            database.Apps.Add(original);

            var context = new OrchestratorContext(database) { SaveException = new IOException("save failed") };
            var operationService = CreateOperationService(Mock.Of<IAclService>());
            var (orchestrator, appConfigService, _, _) = CreateOrchestrator(
                context,
                database,
                operationService);
            orchestrator.Initialize(context);

            var edited = new AppEntry
            {
                Id = "app01",
                Name = "Edited",
                ExePath = @"C:\edited.exe",
                AccountSid = TestAccountSid,
                ManageShortcuts = false
            };
            context.DialogFactory = () => CreateDialog(database, appConfigService.Object, edited.Id);
            context.ShowModal = d =>
            {
                SubmitDialog(d, edited);
                Assert.True(d.HasUnsavedInMemoryMutations);
                Assert.Equal(DialogResult.None, d.DialogResult);
                Assert.Equal("Edited", d.Result.Name);
                Assert.Equal(@"C:\edited.exe", d.Result.ExePath);
            };

            context.CredentialStore.Credentials.Add(new CredentialEntry { Sid = TestAccountSid });
            orchestrator.EditApp(original);

            Assert.Single(database.Apps);
            Assert.Equal("Original", database.Apps.Single().Name);
            Assert.Equal(@"C:\original.exe", database.Apps.Single().ExePath);
        });
    }

    [Fact]
    public void OpenEditDialog_ShortcutCleanupWarning_SavesAndClosesDialog()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var database = new AppDatabase();
            var original = new AppEntry
            {
                Id = "app01",
                Name = "Original",
                ExePath = @"C:\original.exe",
                AccountSid = TestAccountSid,
                ManageShortcuts = true
            };
            database.Apps.Add(original);

            var shortcutService = new Mock<IShortcutService>();
            shortcutService.Setup(s => s.RevertShortcuts(original, It.IsAny<ShortcutTraversalCache>()))
                .Throws(new ShortcutEnforcementException("C:\\ProgramData\\Microsoft\\Windows\\Start Menu\\Programs\\Firefox.lnk: Access denied"));
            var operationService = CreateOperationService(Mock.Of<IAclService>(), shortcutService.Object);

            var context = new OrchestratorContext(database);
            var (orchestrator, appConfigService, messageBox, _) = CreateOrchestrator(
                context,
                database,
                operationService);
            orchestrator.Initialize(context);

            var edited = new AppEntry
            {
                Id = "app01",
                Name = "Edited",
                ExePath = @"C:\edited.exe",
                AccountSid = TestAccountSid,
                ManageShortcuts = true
            };
            context.DialogFactory = () => CreateDialog(database, appConfigService.Object, edited.Id);
            context.ShowModal = d =>
            {
                SubmitDialog(d, edited);
                Assert.False(d.HasUnsavedInMemoryMutations);
                Assert.Equal(DialogResult.OK, d.DialogResult);
            };

            context.CredentialStore.Credentials.Add(new CredentialEntry { Sid = TestAccountSid });
            orchestrator.EditApp(original);

            Assert.Single(database.Apps);
            Assert.Equal("Edited", database.Apps.Single().Name);
            Assert.Single(context.SaveCalls);
            Assert.Empty(context.RefreshCalls);
            messageBox.Verify(
                m => m.Show(
                    It.Is<string>(s => s.Contains("Application was saved, but enforcement failed and needs retry")),
                    "Saved With Warning",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning),
                Times.Once);
        });
    }

    [Fact]
    public void OpenEditDialog_MissingApp_DoesNotCloseDialogOrLaunchAndShowsSaveFailure()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var database = new AppDatabase();
            var original = new AppEntry
            {
                Id = "app01",
                Name = "Original",
                ExePath = @"C:\original.exe",
                AccountSid = TestAccountSid,
                ManageShortcuts = false
            };

            var context = new OrchestratorContext(database);
            var operationService = CreateOperationService(Mock.Of<IAclService>());
            var (orchestrator, appConfigService, messageBox, _) = CreateOrchestrator(
                context,
                database,
                operationService);
            orchestrator.Initialize(context);

            var edited = new AppEntry
            {
                Id = "app01",
                Name = "Edited",
                ExePath = @"C:\edited.exe",
                AccountSid = TestAccountSid,
                ManageShortcuts = false
            };
            context.DialogFactory = () => CreateDialog(database, appConfigService.Object, edited.Id);
            context.ShowModal = d =>
            {
                SubmitDialog(d, edited);
                Assert.True(d.HasUnsavedInMemoryMutations);
                Assert.Equal(DialogResult.None, d.DialogResult);
                Assert.Equal(
                    "Failed: The application no longer exists.",
                    FindControlText<Label>(d, "Failed: The application no longer exists."));
            };

            context.CredentialStore.Credentials.Add(new CredentialEntry { Sid = TestAccountSid });
            orchestrator.EditApp(original);

            Assert.Empty(database.Apps);
            Assert.Empty(context.SaveCalls);
            Assert.Single(context.RefreshCalls);
            Assert.Equal(("app01", -1), context.RefreshCalls.Single());
            Assert.False(context.LaunchCalled);
            messageBox.Verify(
                m => m.Show(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageBoxButtons>(), It.IsAny<MessageBoxIcon>()),
                Times.Never);
        });
    }

    [Fact]
    public void OpenEditDialog_RemoveFailureKeepsAppRemovedAndWarns()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var database = new AppDatabase();
            var existing = new AppEntry
            {
                Id = "app01",
                Name = "App",
                ExePath = @"C:\app.exe",
                AccountSid = TestAccountSid,
                ManageShortcuts = false
            };
            database.Apps.Add(existing);

            var appConfigService = new Mock<IAppConfigService>();
            appConfigService.SetupGet(s => s.HasLoadedConfigs).Returns(false);
            appConfigService.Setup(s => s.GetConfigPath(It.IsAny<string>())).Returns((string? _) => null);
            appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([]);
            appConfigService.Setup(s => s.RemoveApp(It.IsAny<string>()));
            var iconService = new Mock<IIconService>();
            var permissionPrompter = new AppEntryPermissionPrompter(
                Mock.Of<ILoggingService>(),
                Mock.Of<IAclPermissionService>(),
                Mock.Of<IPathGrantService>(),
                new LambdaDatabaseProvider(() => database),
                Mock.Of<IQuickAccessPinService>());

            var context = new OrchestratorContext(database) { SaveException = new IOException("delete failed") };
            var operationService = CreateOperationService(Mock.Of<IAclService>());
            var messageBox = new Mock<IMessageBoxService>();
            var licenseService = new Mock<ILicenseService>();
            licenseService.Setup(s => s.CanAddApp(It.IsAny<int>())).Returns(true);
            licenseService.Setup(s => s.GetRestrictionMessage(It.IsAny<EvaluationFeature>(), It.IsAny<int>())).Returns("license message");
            var orchestrator = new ApplicationsCrudOrchestrator(
                () => CreateDialog(database, appConfigService.Object),
                iconService.Object,
                appConfigService.Object,
                operationService,
                new Mock<IShortcutDiscoveryService>().Object,
                permissionPrompter,
                messageBox.Object,
                licenseService.Object);
            context.DialogFactory = () => CreateDialog(database, appConfigService.Object);
            context.Grid.Columns.Add("name", "Name");
            context.Grid.Rows.Add(new DataGridViewRow { Tag = existing });
            context.Grid.Rows[0].Selected = true;
            orchestrator.Initialize(context);
            context.ShowModal = d =>
            {
                TriggerRemove(d);
                StaTestHelper.PumpUntil(
                    () => context.SaveCalls.Count == 1,
                    timeoutMessage: "Timed out waiting for remove submission to persist.");
            };

            context.CredentialStore.Credentials.Add(new CredentialEntry { Sid = TestAccountSid });
            orchestrator.EditApp(existing);

            Assert.Empty(database.Apps);
            Assert.Single(context.SaveCalls);
            Assert.Single(context.RefreshCalls);
            Assert.Equal((string?)null, context.RefreshCalls.Single().selectAppId);
            Assert.Equal(0, context.RefreshCalls.Single().fallbackIndex);
            messageBox.Verify(
                m => m.Show(
                    It.Is<string>(s => s.Contains("RunFence could not save the change")),
                    "Save Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error),
                Times.Once);
            iconService.Verify(s => s.DeleteIcon("app01"), Times.Once);
            appConfigService.Verify(s => s.RemoveApp("app01"), Times.Once);
        });
    }

    [Fact]
    public void RestoreEnforcementAfterFailedEdit_ReturnsWarningWithoutChangingRetryStatus()
    {
        var database = new AppDatabase();
        var previousApp = new AppEntry
        {
            Id = "app01",
            Name = "Original",
            ExePath = @"C:\original.exe",
            RestrictAcl = false,
            EnforcementRetryStatus = new AppEnforcementRetryStatus("prior", DateTime.UtcNow)
        };

        var aclService = new Mock<IAclService>();
        aclService.Setup(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()))
            .Throws(new InvalidOperationException("recompute failed"));
        var operationService = CreateOperationService(aclService.Object);
        database.Apps.Add(previousApp);

        var result = operationService.RestoreEnforcementAfterFailedEdit(
            previousApp,
            database.Apps,
            new ShortcutTraversalCache([]));

        Assert.Equal(ApplicationsCrudOperationStatus.SucceededWithEnforcementWarning, result.Status);
        Assert.Equal("recompute failed", result.WarningMessage);
        Assert.NotNull(previousApp.EnforcementRetryStatus);
        Assert.Equal("prior", previousApp.EnforcementRetryStatus!.FailureMessage);
    }

    [Fact]
    public void OpenEditDialog_SaveFailure_RestoresSystemStateBeforeDatabaseRollback()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var database = new AppDatabase();
            var original = new AppEntry
            {
                Id = "app01",
                Name = "Original",
                ExePath = @"C:\original.exe",
                AccountSid = TestAccountSid,
                ManageShortcuts = false,
                RestrictAcl = true
            };
            database.Apps.Add(original);

            var callOrder = new List<string>();
            var aclService = new Mock<IAclService>();
            aclService.Setup(s => s.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()))
                .Callback<AppEntry, IReadOnlyList<AppEntry>>((entry, _) =>
                {
                    callOrder.Add($"apply:{entry.Name}");
                    Assert.Equal("Edited", database.Apps.Single().Name);
                });
            aclService.Setup(s => s.RecomputeAllAncestorAcls(It.IsAny<IReadOnlyList<AppEntry>>()))
                .Callback<IReadOnlyList<AppEntry>>(_ =>
                {
                    callOrder.Add($"recompute:{database.Apps.Single().Name}");
                });
            var operationService = CreateOperationService(aclService.Object);

            var context = new OrchestratorContext(database) { SaveException = new IOException("save failed") };
            var (orchestrator, appConfigService, _, _) = CreateOrchestrator(
                context,
                database,
                operationService);
            orchestrator.Initialize(context);

            var edited = new AppEntry
            {
                Id = "app01",
                Name = "Edited",
                ExePath = @"C:\edited.exe",
                AccountSid = TestAccountSid,
                ManageShortcuts = false,
                RestrictAcl = true
            };
            context.DialogFactory = () => CreateDialog(database, appConfigService.Object, edited.Id);
            context.ShowModal = d => SubmitDialog(d, edited);

            context.CredentialStore.Credentials.Add(new CredentialEntry { Sid = TestAccountSid });
            orchestrator.EditApp(original);

            Assert.Contains("apply:Original", callOrder);
            Assert.Contains("recompute:Edited", callOrder);
            Assert.True(
                callOrder.IndexOf("apply:Original") < callOrder.IndexOf("recompute:Edited"),
                "Previous system state must be restored before the database entry is rolled back.");
            Assert.Equal("Original", database.Apps.Single().Name);
        });
    }

    [Fact]
    public void RevertChanges_ShortcutWarningWithoutOptIn_RemainsEnforcementFailure()
    {
        var database = new AppDatabase();
        var app = new AppEntry
        {
            Id = "app01",
            Name = "App",
            ExePath = @"C:\app.exe",
            ManageShortcuts = true
        };
        database.Apps.Add(app);

        var shortcutService = new Mock<IShortcutService>();
        shortcutService.Setup(s => s.RevertShortcuts(app, It.IsAny<ShortcutTraversalCache>()))
            .Throws(new ShortcutEnforcementException("shortcut warning"));
        var operationService = CreateOperationService(Mock.Of<IAclService>(), shortcutService.Object);

        var result = operationService.RevertChanges(new RecordingContext(database), app, new ShortcutTraversalCache([]));

        Assert.Equal(ApplicationsCrudOperationStatus.EnforcementFailed, result.Status);
        Assert.Equal("shortcut warning", result.ErrorMessage);
    }

    private static (ApplicationsCrudOrchestrator, Mock<IAppConfigService>, Mock<IMessageBoxService>, Mock<ILicenseService>) CreateOrchestrator(
        OrchestratorContext context,
        AppDatabase database,
        ApplicationsCrudOperationService operationService)
    {
        var appConfigService = new Mock<IAppConfigService>();
        appConfigService.SetupGet(s => s.HasLoadedConfigs).Returns(false);
        appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([]);
        appConfigService.Setup(s => s.GetConfigPath(It.IsAny<string>())).Returns((string? _) => null);

        var permissionPrompter = new AppEntryPermissionPrompter(
            Mock.Of<ILoggingService>(),
            Mock.Of<IAclPermissionService>(),
            Mock.Of<IPathGrantService>(),
            new LambdaDatabaseProvider(() => database),
            Mock.Of<IQuickAccessPinService>());

        var messageBox = new Mock<IMessageBoxService>();
        messageBox.Setup(m => m.Show(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageBoxButtons>(), It.IsAny<MessageBoxIcon>()));

        var licenseService = new Mock<ILicenseService>();
        licenseService.Setup(s => s.CanAddApp(It.IsAny<int>())).Returns(true);
        licenseService.Setup(s => s.GetRestrictionMessage(EvaluationFeature.Apps, It.IsAny<int>()))
            .Returns("license message");

        var orchestrator = new ApplicationsCrudOrchestrator(
            () => context.DialogFactory?.Invoke() ?? CreateDialog(database, appConfigService.Object),
            new Mock<IIconService>().Object,
            appConfigService.Object,
            operationService,
            new Mock<IShortcutDiscoveryService>().Object,
            permissionPrompter,
            messageBox.Object,
            licenseService.Object);

        return (orchestrator, appConfigService, messageBox, licenseService);
    }

    private static ApplicationsCrudOperationService CreateOperationService(
        IAclService aclService,
        IShortcutService? shortcutService = null,
        IBesideTargetShortcutService? besideTargetShortcutService = null)
    {
        var enforcementHelper = new AppEntryEnforcementHelper(
            aclService,
            shortcutService ?? Mock.Of<IShortcutService>(),
            besideTargetShortcutService ?? Mock.Of<IBesideTargetShortcutService>(),
            Mock.Of<IIconService>(icon => icon.CreateBadgedIcon(It.IsAny<AppEntry>()) == string.Empty),
            Mock.Of<ISidNameCacheService>(),
            Mock.Of<IInteractiveUserDesktopProvider>(),
            Mock.Of<IInteractiveUserSidResolver>(),
            Mock.Of<ILoggingService>());
        return new ApplicationsCrudOperationService(
            aclService,
            enforcementHelper,
            Mock.Of<ILoggingService>());
    }

    private static AppEditDialog CreateDialog(AppDatabase database, IAppConfigService appConfigService, string generatedId = "app1")
    {
        var sidResolver = new Mock<ISidResolver>();
        sidResolver.Setup(s => s.TryResolveName(It.IsAny<string>())).Returns<string>(sid => sid);
        var profilePathResolver = new Mock<IProfilePathResolver>();
        var displayNameResolver = new SidDisplayNameResolver(sidResolver.Object, profilePathResolver.Object);
        var idGenerator = new Mock<IAppEntryIdGenerator>();
        idGenerator.Setup(g => g.GenerateUniqueId(It.IsAny<IEnumerable<string>>())).Returns(generatedId);

        var aclService = new Mock<IAclService>();
        aclService.Setup(s => s.IsBlockedPath(It.IsAny<string>())).Returns(false);
        aclService.Setup(s => s.ResolveAclTargetPath(It.IsAny<AppEntry>())).Returns<AppEntry>(app => app.ExePath);
        var aclConfigValidator = new AclConfigValidator(aclService.Object, Mock.Of<ILoggingService>());

        var aclSection = new AclConfigSection(
            new AclAllowListGridHandler(),
            new AllowListEntryFactory(
                Mock.Of<ILocalUserProvider>(),
                Mock.Of<ILocalGroupMembershipService>(),
                Mock.Of<ISidEntryHelper>(),
                displayNameResolver),
            aclConfigValidator,
            new FolderDepthHelper(aclService.Object, Mock.Of<ILoggingService>()));

        var browseHelper = new AppEditBrowseHelper(
            Mock.Of<IShortcutDiscoveryService>(),
            Mock.Of<IShortcutIconHelper>(),
            new ShortcutTargetResolver(Mock.Of<IShortcutComHelper>()),
            new LambdaSessionProvider(() => new SessionContext
{
                Database = database,
                CredentialStore = new CredentialStore(),
            }.WithOwnedPinDerivedKey(TestSecretFactory.Create(32))),
            Mock.Of<IExecutableKindService>());

        var associationHandler = CreateAssociationHandler(database);
        var saveHandler = new AppEditDialogSaveHandler(associationHandler, appConfigService);
        var executablePathResolver = new Mock<IExecutablePathResolver>();
        executablePathResolver.Setup(r => r.TryResolvePath(It.IsAny<string>(), It.IsAny<ExecutablePathResolutionContext>()))
            .Returns<string, ExecutablePathResolutionContext>((path, _) => path);

        var controller = new AppEditDialogController(
            new AppEntryBuilder(idGenerator.Object),
            executablePathResolver.Object,
            new AppEditDialogInputValidator(),
            new AppEditDialogAclConfigBuilder(aclConfigValidator));
        var submitController = new AppEditDialogSubmitController(
            controller,
            saveHandler);

        var credentialDisplayItemFactory = new CredentialDisplayItemFactory(sidResolver.Object, profilePathResolver.Object);
        var populator = new AppEditDialogPopulator(
            appConfigService,
            credentialDisplayItemFactory,
            new CredentialFilterHelper(sidResolver.Object));
        var initializer = new AppEditDialogInitializer(
            new AppEditPopulator(),
            idGenerator.Object,
            appConfigService,
            associationHandler);
        var initializationBinder = new AppEditDialogInitializationBinder(
            populator,
            initializer,
            credentialDisplayItemFactory,
            () => new IpcCallerSection(
                () => [],
                Mock.Of<ISidEntryHelper>(),
                displayNameResolver));

        return new AppEditDialog(
            appConfigService,
            aclSection,
            browseHelper,
            new AppEditAccountSwitchHandler(),
            controller,
            submitController,
            executablePathResolver.Object,
            new HandlerAssociationsSection(),
            initializationBinder,
            Mock.Of<IUserConfirmationService>(service => service.Confirm(It.IsAny<string>(), It.IsAny<string>()) == true));
    }

    private static AppEditAssociationHandler CreateAssociationHandler(AppDatabase database)
    {
        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>(StringComparer.OrdinalIgnoreCase));
        handlerMappingService.Setup(s => s.GetEffectiveHandlerMappings(database))
            .Returns(new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase));

        var databaseProvider = new LambdaDatabaseProvider(() => database);
        return new AppEditAssociationHandler(
            handlerMappingService.Object,
            Mock.Of<IAppHandlerRegistrationService>(),
            Mock.Of<IAssociationAutoSetService>(),
            databaseProvider,
            () => new HandlerMappingMutationHandler(handlerMappingService.Object));
    }

    private static void SubmitDialog(AppEditDialog dialog, AppEntry result)
    {
        StaTestHelper.CreateControlTree(dialog);
        FindTextBoxBelowLabel(dialog, "Name:").Text = result.Name;
        FindTextBoxBelowLabel(dialog, "File Path or URL:").Text = result.ExePath;
        StaTestHelper.RunAsyncWithMessagePump(() => InvokeHandleOkAsync(dialog));
    }

    private static void TriggerRemove(AppEditDialog dialog)
    {
        StaTestHelper.CreateControlTree(dialog);
        StaTestHelper.RunAsyncWithMessagePump(() => InvokeHandleRemoveAsync(dialog));
    }

    private static TextBox FindTextBoxBelowLabel(Control root, string labelText)
    {
        var label = FindControls<Label>(root)
            .Single(control => string.Equals(control.Text, labelText, StringComparison.Ordinal));

        return FindControls<TextBox>(root)
            .Where(control => control.Parent == label.Parent && control.Top > label.Top)
            .OrderBy(control => control.Top - label.Top)
            .First();
    }

    private static string FindControlText<T>(Control root, string expectedText) where T : Control
    {
        return FindControls<T>(root)
            .Select(control => control.Text)
            .Single(text => string.Equals(text, expectedText, StringComparison.Ordinal));
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "HandleOkAsync")]
    private static extern Task InvokeHandleOkAsync(AppEditDialog dialog);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "HandleRemoveAsync")]
    private static extern Task InvokeHandleRemoveAsync(AppEditDialog dialog);

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

    private sealed class OrchestratorContext(AppDatabase database) : IApplicationsPanelContext
    {
        public AppDatabase Database { get; } = database;
        public CredentialStore CredentialStore { get; } = new();
        public DataGridView Grid { get; } = new();
        public List<(string? selectAppId, int fallbackIndex, bool targetedSave)> SaveCalls { get; } = [];
        public List<(string? selectAppId, int fallbackIndex)> RefreshCalls { get; } = [];

        public Func<AppEditDialog>? DialogFactory { get; set; }
        public Action<AppEditDialog>? ShowModal { get; set; }
        public Exception? SaveException { get; set; }
        public bool LaunchCalled { get; private set; }

        public void ShowModalDialog(Form dialog)
        {
            var appEditDialog = (AppEditDialog)dialog;
            ShowModal?.Invoke(appEditDialog);
        }

        public void SaveAndRefresh(string? selectAppId = null, int fallbackIndex = -1, bool targetedSave = false)
        {
            SaveCalls.Add((selectAppId, fallbackIndex, targetedSave));
            if (SaveException != null)
                throw SaveException;
        }

        public void RefreshAfterInMemoryMutation(string? selectAppId = null, int fallbackIndex = -1)
        {
            RefreshCalls.Add((selectAppId, fallbackIndex));
        }
        public void LaunchApp(AppEntry app, string? launcherArguments)
            => LaunchCalled = true;
    }

    private sealed class RecordingContext(AppDatabase database) : IApplicationMutationContext
    {
        public AppDatabase Database { get; } = database;

        public void SaveAndRefresh(string? selectAppId = null, int fallbackIndex = -1, bool targetedSave = false)
        {
            throw new IOException("save failed");
        }

        public void RefreshAfterInMemoryMutation(string? selectAppId = null, int fallbackIndex = -1)
        {
        }
    }
}
