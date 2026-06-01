using Moq;
using RunFence.Account;
using RunFence.Account.Lifecycle;
using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Acl;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

[Collection("Clipboard")]
public class AccountContainerOrchestratorTests
{
    [Fact]
    public void CreateContainer_AfterOsSaveFailure_RefreshesAfterSave()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var modalCoordinator = new AutoSubmitModalCoordinator();
            var messageBox = new Mock<IAccountMessageBoxService>();
            var dialogNotifier = new Mock<IAppContainerEditDialogNotifier>();
            var saved = false;
            var appContainerService = new Mock<IAppContainerService>();
            appContainerService
                .Setup(s => s.CreateProfile(It.IsAny<AppContainerEntry>()))
                .Returns(AppContainerProfileSetupResult.Success(profileCreatedOrAlreadyExists: true));
            appContainerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");

            var db = new AppDatabase();
            var saveCount = 0;
            var databaseService = new Mock<IDatabaseService>();
            databaseService
                .Setup(s => s.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
                .Callback(() =>
                {
                    saveCount++;
                    if (saveCount == 2)
                        throw new InvalidOperationException("final save failed");
                });

            var orchestrator = CreateOrchestrator(
                modalCoordinator,
                db,
                messageBox.Object,
                dialogNotifier.Object,
                appContainerService,
                databaseService);

            orchestrator.CreateContainer(null, () => saved = true);

            Assert.True(saved);
            messageBox.Verify(
                service => service.Show(
                    null,
                    It.IsAny<string>(),
                    "AppContainer Security Reminder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button1),
                Times.Once);
            modalCoordinator.VerifyDialogSeen();
        });
    }

    [Fact]
    public void CreateContainer_PreOsSaveFailure_DoesNotRefresh()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var modalCoordinator = new AutoSubmitModalCoordinator();
            var messageBox = new Mock<IAccountMessageBoxService>();
            var dialogNotifier = new Mock<IAppContainerEditDialogNotifier>();
            var saved = false;
            var shellHelper = new Mock<IShellHelper>();
            var db = new AppDatabase();
            var databaseService = new Mock<IDatabaseService>();
            databaseService
                .Setup(s => s.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
                .Throws(new InvalidOperationException("save failed"));

            var orchestrator = CreateOrchestrator(
                modalCoordinator,
                db,
                messageBox.Object,
                dialogNotifier.Object,
                databaseService: databaseService);

            orchestrator.CreateContainer(null, () => saved = true);

            Assert.False(saved);
            modalCoordinator.VerifyDialogSeen();
        });
    }

    [Fact]
    public void EditContainer_AfterOsSaveFailure_RefreshesAfterSave()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var modalCoordinator = new AutoSubmitModalCoordinator(ModifyContainerDialogForAfterOsEdit);
            var messageBox = new Mock<IAccountMessageBoxService>();
            var dialogNotifier = new Mock<IAppContainerEditDialogNotifier>();
            var saved = false;
            var shellHelper = new Mock<IShellHelper>();
            var appContainerService = new Mock<IAppContainerService>();
            appContainerService.Setup(s => s.GetSid(It.IsAny<string>())).Returns("S-1-15-2-1");
            appContainerService
                .Setup(s => s.RevokeComAccess("S-1-15-2-1", "{CLSID-1}"))
                .Returns(AppContainerComAccessResult.Success());

            var databaseService = new Mock<IDatabaseService>();
            databaseService
                .Setup(s => s.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
                .Throws(new InvalidOperationException("edit final save failed"));

            var db = new AppDatabase();
            var row = new ContainerRow(
                new AppContainerEntry { Name = "rfn_browser", DisplayName = "Browser", ComAccessClsids = ["{CLSID-1}"] },
                "S-1-15-2-42");
            db.AppContainers.Add(row.Container);

            var orchestrator = CreateOrchestrator(
                modalCoordinator,
                db,
                messageBox.Object,
                dialogNotifier.Object,
                appContainerService,
                databaseService);

            await orchestrator.EditContainer(row, null, () => saved = true);

            Assert.True(saved);
            modalCoordinator.VerifyDialogSeen();
        });
    }

    [Fact]
    public void CreateContainer_LicenseBlocked_DoesNotOpenDialogOrRefresh()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var modalCoordinator = new AutoSubmitModalCoordinator();
            var messageBox = new Mock<IAccountMessageBoxService>();
            var dialogNotifier = new Mock<IAppContainerEditDialogNotifier>();
            var saved = false;
            var shellHelper = new Mock<IShellHelper>();
            var db = new AppDatabase();

            var orchestrator = CreateOrchestrator(
                modalCoordinator,
                db,
                messageBox.Object,
                dialogNotifier.Object,
                canCreateContainer: false);

            orchestrator.CreateContainer(null, () => saved = true);

            Assert.False(saved);
            Assert.False(modalCoordinator.DialogSeen);
            messageBox.Verify(
                service => service.Show(
                    null,
                    It.IsAny<string>(),
                    "License Limit",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button1),
                Times.Once);
        });
    }

    [Fact]
    public void EditContainer_PreOsSaveFailure_DoesNotRefresh()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var modalCoordinator = new AutoSubmitModalCoordinator();
            var messageBox = new Mock<IAccountMessageBoxService>();
            var dialogNotifier = new Mock<IAppContainerEditDialogNotifier>();
            var saved = false;
            var shellHelper = new Mock<IShellHelper>();
            var databaseService = new Mock<IDatabaseService>();
            databaseService
                .Setup(s => s.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
                .Throws(new InvalidOperationException("edit pre-os failed"));

            var db = new AppDatabase();
            var row = new ContainerRow(
                new AppContainerEntry { Name = "rfn_browser", DisplayName = "Browser", ComAccessClsids = ["{CLSID-1}"] },
                "S-1-15-2-42");
            db.AppContainers.Add(row.Container);

            var orchestrator = CreateOrchestrator(
                modalCoordinator,
                db,
                messageBox.Object,
                dialogNotifier.Object,
                databaseService: databaseService);

            await orchestrator.EditContainer(row, null, () => saved = true);

            Assert.False(saved);
            modalCoordinator.VerifyDialogSeen();
        });
    }

    [Fact]
    public void EditContainer_DeleteRequested_RoutesToDeleteContainer()
    {
        StaTestHelper.RunAsyncOnSta(async () =>
        {
            var modalCoordinator = new AutoSubmitModalCoordinator(ClickDeleteButton);
            var messageBox = new Mock<IAccountMessageBoxService>();
            messageBox
                .Setup(service => service.Show(
                    null,
                    It.IsAny<string>(),
                    "Confirm Delete Container",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button1))
                .Returns(DialogResult.Yes);

            var containerDeletion = new Mock<IContainerDeletionService>();
            containerDeletion
                .Setup(service => service.DeleteContainer(It.IsAny<AppContainerEntry>(), It.IsAny<string?>()))
                .ReturnsAsync(ContainerDeletionResult.Success());

            var saved = false;
            var shellHelper = new Mock<IShellHelper>();
            var db = new AppDatabase();
            var row = new ContainerRow(
                new AppContainerEntry { Name = "rfn_browser", DisplayName = "Browser" },
                "S-1-15-2-42");
            db.AppContainers.Add(row.Container);

            var orchestrator = CreateOrchestrator(
                modalCoordinator,
                db,
                messageBox.Object,
                new Mock<IAppContainerEditDialogNotifier>().Object,
                containerDeletionService: containerDeletion);

            await orchestrator.EditContainer(row, null, () => saved = true);

            Assert.True(saved);
            containerDeletion.Verify(service => service.DeleteContainer(row.Container, row.ContainerSid), Times.Once);
            modalCoordinator.VerifyDialogSeen();
        });
    }

    private static AccountContainerOrchestrator CreateOrchestrator(
        IModalCoordinator modalCoordinator,
        AppDatabase db,
        IAccountMessageBoxService messageBoxService,
        IAppContainerEditDialogNotifier dialogNotifier,
        Mock<IAppContainerService>? appContainerService = null,
        Mock<IDatabaseService>? databaseService = null,
        Mock<IContainerDeletionService>? containerDeletionService = null,
        bool canCreateContainer = true)
    {
        var persistenceHelper = new SessionPersistenceHelper(
            new Mock<IConfigReencryptionPersistence>().Object,
            new Mock<IMainConfigPersistence>().Object,
            new Mock<ISidNameCacheService>().Object,
            () => new InlineUiThreadInvoker(action => action()),
            new Mock<ILoggingService>().Object);

        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
{
            Database = db,
            CredentialStore = new CredentialStore(),
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32)));

        var effectiveAppContainerService = appContainerService ?? new Mock<IAppContainerService>();
        var log = new Mock<ILoggingService>();
        var effectiveDatabaseService = databaseService ?? new Mock<IDatabaseService>();
        var editService = new AppContainerEditService(
            effectiveAppContainerService.Object,
            new LambdaDatabaseProvider(() => db),
            log.Object,
            effectiveDatabaseService.Object,
            sessionProvider.Object);

        var aclManagerLauncher = new AccountAclManagerLauncher(
            () => throw new NotSupportedException(),
            new Mock<ISidNameCacheService>().Object);

        var licenseService = new Mock<ILicenseService>();
        licenseService
            .Setup(l => l.CanCreateContainer(It.IsAny<int>()))
            .Returns(canCreateContainer);
        licenseService
            .Setup(l => l.GetRestrictionMessage(EvaluationFeature.Containers, It.IsAny<int>()))
            .Returns("license blocked");

        var enforcementCoordinator = AppEntryEnforcementTestFactory.CreateCoordinator(
            new Mock<RunFence.Acl.IAclService>().Object,
            new Mock<IShortcutService>().Object,
            new Mock<IBesideTargetShortcutService>().Object,
            new Mock<IIconService>().Object,
            new Mock<ISidNameCacheService>().Object,
            new Mock<IInteractiveUserDesktopProvider>().Object,
            new Mock<IInteractiveUserSidResolver>().Object,
            new TestRunFenceLauncherPathProvider(@"C:\RunFence\RunFence.Launcher.exe", exists: true),
            new Mock<ILoggingService>().Object);
        var cleanupHelper = new ContainerDeletionCleanupHelper(
            enforcementCoordinator,
            new Mock<RunFence.Acl.IAclService>().Object,
            new Mock<IIconService>().Object,
            new Mock<IShortcutDiscoveryService>().Object,
            new Mock<ILoggingService>().Object);
        var dialogRunner = new AppContainerEditDialogRunner(
            modalCoordinator,
            () => new AppContainerEditDialog(
                new AppContainerEditSubmitController(editService),
                new AppContainerDialogStateAssembler(),
                new AppContainerCapabilitiesBinder(dialogNotifier),
                new AppContainerDialogResultPresenter(dialogNotifier)),
            licenseService.Object,
            messageBoxService,
            sessionProvider.Object);
        return new AccountContainerOrchestrator(
            persistenceHelper,
            (containerDeletionService ?? new Mock<IContainerDeletionService>()).Object,
            dialogRunner,
            aclManagerLauncher,
            cleanupHelper,
            sessionProvider.Object,
            messageBoxService);
    }

    private sealed class AutoSubmitModalCoordinator(Action<AppContainerEditDialog>? configureDialog = null) : IModalCoordinator
    {
        public bool DialogSeen { get; private set; }

        public bool BeginCalled { get; private set; }

        public bool EndCalled { get; private set; }

        public void BeginModal() => BeginCalled = true;

        public void EndModal() => EndCalled = true;

        public DialogResult ShowModal(Form dialog, IWin32Window? owner)
        {
            DialogSeen = true;

            if (dialog is AppContainerEditDialog containerDialog)
            {
                StaTestHelper.CreateControlTree(containerDialog);
                SetDisplayName(containerDialog, "Browser");
                configureDialog?.Invoke(containerDialog);
                if (!containerDialog.DeleteRequested)
                {
                    ClickButton(containerDialog, "OK");
                    StaTestHelper.PumpUntil(
                        () => containerDialog.DialogResult != DialogResult.None || !containerDialog.UseWaitCursor,
                        timeoutMessage: "Timed out waiting for AppContainer dialog submission.");
                }
                return containerDialog.DialogResult;
            }

            return DialogResult.Cancel;
        }

        public void RunModal(Action action) => action();

        public void RunOnSecureDesktop(Action action) => action();

        public void VerifyDialogSeen()
        {
            Assert.True(DialogSeen);
        }
    }

    private static void ModifyContainerDialogForAfterOsEdit(AppContainerEditDialog containerDialog)
    {
        var comList = EnumerateControls(containerDialog).OfType<ListBox>().FirstOrDefault();
        if (comList?.Items.Count > 0)
            comList.Items.RemoveAt(0);
    }

    private static void ClickDeleteButton(AppContainerEditDialog containerDialog)
    {
        ClickButton(containerDialog, "Delete Container");
    }

    private static void SetDisplayName(Control root, string displayName)
    {
        var layout = EnumerateControls(root).OfType<TableLayoutPanel>().Single();
        var displayNameBox = (TextBox)layout.GetControlFromPosition(1, 0)!;
        Assert.NotNull(displayNameBox);
        displayNameBox.Text = displayName;
    }

    private static void ClickButton(Control root, string text)
    {
        var button = EnumerateControls(root).OfType<Button>().Single(b => b.Text == text);
        StaTestHelper.ClickButton(button);
    }

    private static IEnumerable<Control> EnumerateControls(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var grandchild in EnumerateControls(child))
                yield return grandchild;
        }
    }
}
