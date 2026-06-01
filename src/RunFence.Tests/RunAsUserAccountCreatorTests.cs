using System.Security.AccessControl;
using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Acl.Permissions;
using RunFence.Acl.UI;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Firewall.UI;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.PrefTrans;
using RunFence.RunAs;
using RunFence.Security;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public sealed class RunAsUserAccountCreatorTests : IDisposable
{
    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);
    private readonly List<SessionContext> _sessions = [];

    public void Dispose()
    {
        foreach (var session in _sessions)
            session.Dispose();

        _pinKey.Dispose();
    }

    [Fact]
    public async Task CreateNewAccount_CleanupStateSaveFailed_ShowsWarningAndNotifies()
    {
        var database = new AppDatabase();
        var session = CreateSession(database);

        var dialog = new TestCreateAccountDialog
        {
            CreatedAccountStatus = CreateAccountStatus.CleanupStateSaveFailed
        };

        var creationUi = new Mock<IRunAsAccountCreationUI>();
        creationUi.Setup(u => u.ShowCreateAccountDialog(It.IsAny<string>()))
            .Returns(new ShowCreateAccountResult(
                dialog,
                WasCancelled: false,
                Status: CreateAccountStatus.CleanupStateSaveFailed,
                ErrorMessage: "save failed"));

        var appState = new Mock<IAppStateProvider>();
        appState.SetupGet(a => a.Database).Returns(database);

        var dataChangeNotifier = new Mock<IDataChangeNotifier>();
        var messageBoxService = new Mock<IAccountMessageBoxService>();
        var modalCoordinator = new Mock<IModalCoordinator>();
        var creator = CreateCreator(
            appState.Object,
            session,
            creationUi.Object,
            dataChangeNotifier.Object,
            messageBoxService.Object,
            modalCoordinator.Object);

        var result = await creator.CreateNewAccountAsync(@"C:\Apps\tool.exe");

        Assert.Null(result);
        Assert.True(dialog.Disposed);
        dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Once);
        messageBoxService.Verify(
            m => m.Show(
                null,
                "Windows created the account, but RunFence could not save its cleanup state.\n\n" +
                "The account remains in memory for this session only:\nsave failed",
                "Account Created But Not Saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1),
            Times.Once);
        modalCoordinator.Verify(m => m.EndModal(), Times.Once);
    }

    [Fact]
    public async Task CreateNewAccount_Success_ShowsWarningsNotifiesAndReturnsPermissionGrant()
    {
        const string sid = "S-1-5-21-100-200-300-1001";
        var database = new AppDatabase();
        var session = CreateSession(database);
        var rollbackState = CreateRollbackState(sid);
        var permissionGrant = new AncestorPermissionResult(@"C:\Apps", FileSystemRights.ReadAndExecute);

        var dialog = new TestCreateAccountDialog
        {
            CreatedSid = sid,
            NewUsername = "newuser",
            CreatedPassword = ProtectedString.FromChars("P@ssw0rd".AsSpan()),
            CreatedRollbackState = rollbackState,
            Errors = ["Settings import: warning"],
            SelectedPrivilegeLevel = PrivilegeLevel.Basic,
            CreatedAccountStatus = CreateAccountStatus.Succeeded
        };

        var creationUi = new Mock<IRunAsAccountCreationUI>();
        creationUi.Setup(u => u.ShowCreateAccountDialog(It.IsAny<string>()))
            .Returns(new ShowCreateAccountResult(dialog, WasCancelled: false, Status: CreateAccountStatus.Succeeded));

        var appState = new Mock<IAppStateProvider>();
        appState.SetupGet(a => a.Database).Returns(database);

        var dataChangeNotifier = new Mock<IDataChangeNotifier>();
        var messageBoxService = new Mock<IAccountMessageBoxService>();
        var modalCoordinator = new Mock<IModalCoordinator>();
        var permissionPromptHelper = new Mock<IRunAsPermissionPromptHelper>();
        permissionPromptHelper.Setup(p => p.PromptIfNeeded(@"C:\Apps\tool.exe", sid))
            .Returns(permissionGrant);

        var databaseService = new Mock<IDatabaseService>();
        var creator = CreateCreator(
            appState.Object,
            session,
            creationUi.Object,
            dataChangeNotifier.Object,
            messageBoxService.Object,
            modalCoordinator.Object,
            databaseService: databaseService.Object,
            permissionPromptHelper: permissionPromptHelper.Object);

        var result = await creator.CreateNewAccountAsync(@"C:\Apps\tool.exe");

        var created = Assert.IsType<RunAsCreatedAccountResult>(result);
        Assert.Equal(sid, created.Credential.Sid);
        Assert.Same(permissionGrant, created.PermissionGrant);
        Assert.True(dialog.Disposed);
        Assert.Single(session.CredentialStore.Credentials);
        Assert.Equal(PrivilegeLevel.Basic, database.GetAccount(sid)?.PrivilegeLevel);
        dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Once);
        messageBoxService.Verify(
            m => m.Show(
                null,
                "Account created with warnings:\n\nSettings import: warning",
                "RunFence",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1),
            Times.Once);
        databaseService.Verify(s => s.SaveCredentialStore(session.CredentialStore), Times.Once);
        databaseService.Verify(
            s => s.SaveConfig(database, It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()),
            Times.Once);
        modalCoordinator.Verify(m => m.EndModal(), Times.Once);
    }

    [Fact]
    public async Task CreateNewAccount_PostSetupCanceled_ReturnsNullWithoutShowingWarnings()
    {
        const string sid = "S-1-5-21-100-200-300-1001";
        var database = new AppDatabase();
        var session = CreateSession(database);

        var dialog = new TestCreateAccountDialog
        {
            CreatedSid = sid,
            NewUsername = "newuser",
            CreatedPassword = ProtectedString.FromChars("P@ssw0rd".AsSpan()),
            CreatedRollbackState = CreateRollbackState(sid),
            FirewallSettingsChanged = true,
            AllowInternet = false,
            AllowLocalhost = false,
            AllowLan = false,
            Errors = ["Settings import: warning"],
            CreatedAccountStatus = CreateAccountStatus.Succeeded
        };

        var creationUi = new Mock<IRunAsAccountCreationUI>();
        creationUi.Setup(u => u.ShowCreateAccountDialog(It.IsAny<string>()))
            .Returns(new ShowCreateAccountResult(dialog, WasCancelled: false, Status: CreateAccountStatus.Succeeded));

        var appState = new Mock<IAppStateProvider>();
        appState.SetupGet(a => a.Database).Returns(database);

        var dataChangeNotifier = new Mock<IDataChangeNotifier>();
        var messageBoxService = new Mock<IAccountMessageBoxService>();
        var modalCoordinator = new Mock<IModalCoordinator>();
        var permissionPromptHelper = new Mock<IRunAsPermissionPromptHelper>();
        permissionPromptHelper.Setup(p => p.PromptIfNeeded(@"C:\Apps\tool.exe", sid))
            .Throws<OperationCanceledException>();

        var creator = CreateCreator(
            appState.Object,
            session,
            creationUi.Object,
            dataChangeNotifier.Object,
            messageBoxService.Object,
            modalCoordinator.Object,
            permissionPromptHelper: permissionPromptHelper.Object);

        var result = await creator.CreateNewAccountAsync(@"C:\Apps\tool.exe");

        Assert.Null(result);
        Assert.True(dialog.Disposed);
        Assert.Single(session.CredentialStore.Credentials);
        var account = database.GetAccount(sid);
        Assert.Equal(PrivilegeLevel.Isolated, account?.PrivilegeLevel);
        Assert.NotNull(account);
        Assert.False(account!.Firewall.IsDefault);
        Assert.False(account.Firewall.AllowInternet);
        Assert.False(account.Firewall.AllowLocalhost);
        Assert.False(account.Firewall.AllowLan);
        dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Once);
        messageBoxService.Verify(
            m => m.Show(
                It.IsAny<IWin32Window?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<MessageBoxButtons>(),
                It.IsAny<MessageBoxIcon>(),
                It.IsAny<MessageBoxDefaultButton>()),
            Times.Never);
        modalCoordinator.Verify(m => m.EndModal(), Times.Once);
    }

    [Fact]
    public async Task CreateNewAccount_CredentialSaveRolledBack_ShowsRolledBackWarningAndNoNotify()
    {
        const string sid = "S-1-5-21-100-200-300-1001";
        var database = new AppDatabase();
        var session = CreateSession(database);

        var dialog = new TestCreateAccountDialog
        {
            CreatedSid = sid,
            NewUsername = "newuser",
            CreatedPassword = ProtectedString.FromChars("P@ssw0rd".AsSpan()),
            CreatedRollbackState = CreateRollbackState(sid),
            CreatedAccountStatus = CreateAccountStatus.Succeeded
        };

        var creationUi = new Mock<IRunAsAccountCreationUI>();
        creationUi.Setup(u => u.ShowCreateAccountDialog(It.IsAny<string>()))
            .Returns(new ShowCreateAccountResult(dialog, WasCancelled: false, Status: CreateAccountStatus.Succeeded));

        var appState = new Mock<IAppStateProvider>();
        appState.SetupGet(a => a.Database).Returns(database);

        var dataChangeNotifier = new Mock<IDataChangeNotifier>();
        var messageBoxService = new Mock<IAccountMessageBoxService>();
        var modalCoordinator = new Mock<IModalCoordinator>();
        var databaseService = new Mock<IDatabaseService>();
        databaseService.Setup(s => s.SaveCredentialStore(session.CredentialStore))
            .Throws(new InvalidOperationException("save failed"));

        var lifecycleManager = new Mock<IAccountLifecycleManager>();
        lifecycleManager.Setup(s => s.DeleteSamAccount(sid))
            .Returns(new AccountDeletionResult(true, sid, null));
        lifecycleManager.Setup(s => s.DeleteProfileAsync(sid))
            .ReturnsAsync((string?)null);

        var creator = CreateCreator(
            appState.Object,
            session,
            creationUi.Object,
            dataChangeNotifier.Object,
            messageBoxService.Object,
            modalCoordinator.Object,
            databaseService: databaseService.Object,
            lifecycleManager: lifecycleManager.Object);

        var result = await creator.CreateNewAccountAsync(@"C:\Apps\tool.exe");

        Assert.Null(result);
        Assert.True(dialog.Disposed);
        Assert.Empty(session.CredentialStore.Credentials);
        dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Never);
        messageBoxService.Verify(
            m => m.Show(
                null,
                "Windows created the account, but RunFence could not save the credential store.\n\n" +
                "The account was rolled back:\n" +
                "save failed",
                "Account Creation Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1),
            Times.Once);
        modalCoordinator.Verify(m => m.EndModal(), Times.Once);
    }

    [Fact]
    public async Task CreateNewAccount_PrePersistenceRolledBack_ShowsWarningAndNoNotify()
    {
        const string sid = "S-1-5-21-100-200-300-1001";
        var database = new AppDatabase();
        var session = CreateSession(database);

        var dialog = new TestCreateAccountDialog
        {
            CreatedSid = sid,
            NewUsername = "newuser",
            CreatedPassword = ProtectedString.FromChars("P@ssw0rd".AsSpan()),
            CreatedRollbackState = CreateRollbackState(sid),
            CreatedAccountStatus = CreateAccountStatus.Succeeded
        };

        var creationUi = new Mock<IRunAsAccountCreationUI>();
        creationUi.Setup(u => u.ShowCreateAccountDialog(It.IsAny<string>()))
            .Returns(new ShowCreateAccountResult(dialog, WasCancelled: false, Status: CreateAccountStatus.Succeeded));

        var appState = new Mock<IAppStateProvider>();
        appState.SetupGet(a => a.Database).Returns(database);

        var dataChangeNotifier = new Mock<IDataChangeNotifier>();
        var messageBoxService = new Mock<IAccountMessageBoxService>();
        var modalCoordinator = new Mock<IModalCoordinator>();
        var encryptionService = new Mock<IByteArrayCredentialEncryptionService>();
        encryptionService.Setup(s => s.Encrypt(It.IsAny<ProtectedString>(), It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("encrypt failed"));
        var lifecycleManager = new Mock<IAccountLifecycleManager>();
        lifecycleManager.Setup(s => s.DeleteSamAccount(sid))
            .Returns(new AccountDeletionResult(true, sid, null));
        lifecycleManager.Setup(s => s.DeleteProfileAsync(sid))
            .ReturnsAsync((string?)null);

        var creator = CreateCreator(
            appState.Object,
            session,
            creationUi.Object,
            dataChangeNotifier.Object,
            messageBoxService.Object,
            modalCoordinator.Object,
            encryptionService: encryptionService,
            lifecycleManager: lifecycleManager.Object);

        var result = await creator.CreateNewAccountAsync(@"C:\Apps\tool.exe");

        Assert.Null(result);
        Assert.True(dialog.Disposed);
        Assert.Empty(session.CredentialStore.Credentials);
        dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Never);
        messageBoxService.Verify(
            m => m.Show(
                null,
                "Windows created the account, but RunFence failed before credential persistence completed.\n\n" +
                "The account was rolled back:\n" +
                "encrypt failed",
                "Account Creation Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1),
            Times.Once);
        modalCoordinator.Verify(m => m.EndModal(), Times.Once);
    }

    [Fact]
    public async Task CreateNewAccount_PrePersistenceRollbackFailed_ShowsRollbackFailedWarningAndNotifies()
    {
        const string sid = "S-1-5-21-100-200-300-1001";
        var database = new AppDatabase();
        var session = CreateSession(database);

        var dialog = new TestCreateAccountDialog
        {
            CreatedSid = sid,
            NewUsername = "newuser",
            CreatedPassword = ProtectedString.FromChars("P@ssw0rd".AsSpan()),
            CreatedRollbackState = CreateRollbackState(sid),
            CreatedAccountStatus = CreateAccountStatus.Succeeded
        };

        var creationUi = new Mock<IRunAsAccountCreationUI>();
        creationUi.Setup(u => u.ShowCreateAccountDialog(It.IsAny<string>()))
            .Returns(new ShowCreateAccountResult(dialog, WasCancelled: false, Status: CreateAccountStatus.Succeeded));

        var appState = new Mock<IAppStateProvider>();
        appState.SetupGet(a => a.Database).Returns(database);

        var dataChangeNotifier = new Mock<IDataChangeNotifier>();
        var messageBoxService = new Mock<IAccountMessageBoxService>();
        var modalCoordinator = new Mock<IModalCoordinator>();
        var encryptionService = new Mock<IByteArrayCredentialEncryptionService>();
        encryptionService.Setup(s => s.Encrypt(It.IsAny<ProtectedString>(), It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("encrypt failed"));
        var lifecycleManager = new Mock<IAccountLifecycleManager>();
        lifecycleManager.Setup(s => s.DeleteSamAccount(sid))
            .Returns(new AccountDeletionResult(false, sid, "delete failed"));

        var creator = CreateCreator(
            appState.Object,
            session,
            creationUi.Object,
            dataChangeNotifier.Object,
            messageBoxService.Object,
            modalCoordinator.Object,
            encryptionService: encryptionService,
            lifecycleManager: lifecycleManager.Object);

        var result = await creator.CreateNewAccountAsync(@"C:\Apps\tool.exe");

        Assert.Null(result);
        Assert.True(dialog.Disposed);
        Assert.Empty(session.CredentialStore.Credentials);
        dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Once);
        messageBoxService.Verify(
            m => m.Show(
                null,
                "Windows created the account, but RunFence failed before credential persistence completed and rollback also failed.\n\n" +
                "Error: encrypt failed\n" +
                "Rollback error: delete failed",
                "Account Creation Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1),
            Times.Once);
        lifecycleManager.Verify(s => s.DeleteProfileAsync(It.IsAny<string>()), Times.Never);
        modalCoordinator.Verify(m => m.EndModal(), Times.Once);
    }

    [Fact]
    public async Task CreateNewAccount_CredentialSaveRollbackFailed_ShowsFailureAndSchedulesCleanup()
    {
        const string sid = "S-1-5-21-100-200-300-1001";
        var database = new AppDatabase();
        var account = database.GetOrCreateAccount(sid);
        var session = CreateSession(database);

        var dialog = new TestCreateAccountDialog
        {
            CreatedSid = sid,
            NewUsername = "newuser",
            CreatedPassword = ProtectedString.FromChars("P@ssw0rd".AsSpan()),
            CreatedRollbackState = CreateRollbackState(sid),
            CreatedAccountStatus = CreateAccountStatus.Succeeded
        };

        var creationUi = new Mock<IRunAsAccountCreationUI>();
        creationUi.Setup(u => u.ShowCreateAccountDialog(It.IsAny<string>()))
            .Returns(new ShowCreateAccountResult(dialog, WasCancelled: false, Status: CreateAccountStatus.Succeeded));

        var appState = new Mock<IAppStateProvider>();
        appState.SetupGet(a => a.Database).Returns(database);

        var databaseService = new Mock<IDatabaseService>();
        databaseService.Setup(s => s.SaveCredentialStore(session.CredentialStore))
            .Throws(new InvalidOperationException("save failed"));

        var lifecycleManager = new Mock<IAccountLifecycleManager>();
        lifecycleManager.Setup(s => s.DeleteSamAccount(sid))
            .Returns(new AccountDeletionResult(false, sid, "delete failed"));

        var dataChangeNotifier = new Mock<IDataChangeNotifier>();
        var messageBoxService = new Mock<IAccountMessageBoxService>();
        var modalCoordinator = new Mock<IModalCoordinator>();
        var creator = CreateCreator(
            appState.Object,
            session,
            creationUi.Object,
            dataChangeNotifier.Object,
            messageBoxService.Object,
            modalCoordinator.Object,
            databaseService: databaseService.Object,
            lifecycleManager: lifecycleManager.Object);

        var result = await creator.CreateNewAccountAsync(@"C:\Apps\tool.exe");

        Assert.Null(result);
        Assert.True(dialog.Disposed);
        Assert.NotNull(account.DeleteAfterUtc);
        Assert.Single(session.CredentialStore.Credentials);
        dataChangeNotifier.Verify(n => n.NotifyDataChanged(), Times.Once);
        messageBoxService.Verify(
            m => m.Show(
                null,
                "Windows created the account, but RunFence could not save the credential store and rollback also failed.\n\n" +
                "Save error: save failed\n" +
                "Rollback error: delete failed",
                "Account Creation Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1),
            Times.Once);
        modalCoordinator.Verify(m => m.EndModal(), Times.Once);
    }

    private SessionContext CreateSession(AppDatabase database)
    {
        var session = new SessionContext
{
            Database = database,
            CredentialStore = new CredentialStore { ArgonSalt = [1, 2, 3] },
        }.WithClonedPinDerivedKey(_pinKey);
        _sessions.Add(session);
        return session;
    }

    private static CreatedAccountRollbackState CreateRollbackState(string sid)
    {
        return new CreatedAccountRollbackState
        {
            Sid = sid,
            Username = "newuser",
            HadPreviousAccount = false,
            HadPreviousSidName = false,
            HadPreviousFirewallSettings = false
        };
    }

    private RunAsUserAccountCreator CreateCreator(
        IAppStateProvider appState,
        SessionContext session,
        IRunAsAccountCreationUI creationUi,
        IDataChangeNotifier dataChangeNotifier,
        IAccountMessageBoxService messageBoxService,
        IModalCoordinator modalCoordinator,
        Mock<IByteArrayCredentialEncryptionService>? encryptionService = null,
        IDatabaseService? databaseService = null,
        IRunAsPermissionPromptHelper? permissionPromptHelper = null,
        IAccountLifecycleManager? lifecycleManager = null)
    {
        var persistenceCoordinator = CreatePersistenceCoordinator(
            session,
            databaseService,
            lifecycleManager,
            dataChangeNotifier,
            encryptionService);
        var postSetupService = CreatePostSetupService(
            appState,
            session,
            databaseService ?? Mock.Of<IDatabaseService>(),
            permissionPromptHelper ?? Mock.Of<IRunAsPermissionPromptHelper>());
        var errorPresenter = new RunAsAccountCreationErrorPresenter(messageBoxService);

        return new RunAsUserAccountCreator(
            dataChangeNotifier,
            session,
            persistenceCoordinator,
            postSetupService,
            creationUi,
            errorPresenter,
            modalCoordinator);
    }

    private RunAsCreatedAccountPersistenceCoordinator CreatePersistenceCoordinator(
        SessionContext session,
        IDatabaseService? databaseService,
        IAccountLifecycleManager? lifecycleManager,
        IDataChangeNotifier dataChangeNotifier,
        Mock<IByteArrayCredentialEncryptionService>? encryptionService = null)
    {
        var localUserProvider = new Mock<ILocalUserProvider>();
        var encryption = encryptionService ?? CreateEncryptionServiceMock();
        var credentialCreator = new RunAsCredentialCreator(
            session,
            new ByteArrayCredentialEncryptionSpanAdapter(encryption.Object),
            databaseService ?? Mock.Of<IDatabaseService>(),
            localUserProvider.Object,
            Mock.Of<ISidNameCacheService>());

        var rollbackExecutor = new CreatedAccountRollbackExecutor(
            lifecycleManager ?? Mock.Of<IAccountLifecycleManager>(),
            CreateCredentialManager(),
            Mock.Of<IAssociationAutoSetService>(),
            localUserProvider.Object,
            Mock.Of<ILoggingService>());

        return new RunAsCreatedAccountPersistenceCoordinator(
            credentialCreator,
            rollbackExecutor,
            dataChangeNotifier,
            Mock.Of<ILoggingService>());
    }

    private RunAsCreatedAccountPostSetupService CreatePostSetupService(
        IAppStateProvider appState,
        SessionContext session,
        IDatabaseService databaseService,
        IRunAsPermissionPromptHelper permissionPromptHelper)
    {
        var firewallApplyHelper = new FirewallApplyHelper(
            Mock.Of<IAccountFirewallSettingsApplier>(),
            new DynamicPortRangeChecker(Mock.Of<ILoggingService>(), Mock.Of<IUserConfirmationService>(), new StandardNetshCommandRunner()),
            Mock.Of<ILoggingService>());

        var settingsApplier = new RunAsAccountSettingsApplier(
            appState,
            session,
            databaseService,
            Mock.Of<ILoggingService>(),
            Mock.Of<ISettingsTransferService>(),
            firewallApplyHelper,
            new ImmediateAccountCreationProgressRunner());

        return new RunAsCreatedAccountPostSetupService(
            appState,
            settingsApplier,
            permissionPromptHelper);
    }

    private static Mock<IByteArrayCredentialEncryptionService> CreateEncryptionServiceMock()
    {
        var encryptionService = new Mock<IByteArrayCredentialEncryptionService>();
        encryptionService.Setup(s => s.Encrypt(It.IsAny<ProtectedString>(), It.IsAny<byte[]>()))
            .Returns([1, 2, 3]);
        return encryptionService;
    }

    private static IAccountCredentialManager CreateCredentialManager()
    {
        var credentialManager = new Mock<IAccountCredentialManager>();
        credentialManager.Setup(s => s.RemoveCredential(It.IsAny<Guid>(), It.IsAny<CredentialStore>()))
            .Callback<Guid, CredentialStore>((id, store) => store.Credentials.RemoveAll(c => c.Id == id));
        return credentialManager.Object;
    }

    private sealed class TestCreateAccountDialog : IShowCreateAccountResultDialog
    {
        public string? CreatedSid { get; init; }

        public ProtectedString? CreatedPassword { get; init; }

        public string? NewUsername { get; init; }

        public bool IsEphemeral { get; init; }

        public PrivilegeLevel SelectedPrivilegeLevel { get; init; }

        public bool FirewallSettingsChanged { get; init; }

        public bool AllowInternet { get; init; }

        public bool AllowLocalhost { get; init; }

        public bool AllowLan { get; init; }

        public string? SettingsImportPath { get; init; }

        public List<string> Errors { get; init; } = [];

        public CreateAccountStatus CreatedAccountStatus { get; init; }

        public CreatedAccountRollbackState? CreatedRollbackState { get; init; }

        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
