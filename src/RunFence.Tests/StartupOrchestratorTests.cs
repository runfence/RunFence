using Autofac;
using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup;
using RunFence.Startup.UI;
using Xunit;

namespace RunFence.Tests;

public class StartupOrchestratorTests
{
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IDatabaseService> _databaseService = new();
    private readonly Mock<IAppInitializationHelper> _appInit = new();
    private readonly Mock<IRememberPinService> _rememberPinService = new();
    private readonly Mock<IStartupUI> _startupUi = new();
    private readonly Mock<IStartupSessionScopeFactory> _scopeFactory = new();
    private readonly Mock<IStartupMainFormRunner> _mainFormRunner = new();
    private readonly Mock<IStartupCredentialLoader> _credentialLoader = new();

    private readonly CredentialStore _store = new();

    private static readonly byte[] ValidKey32 = new byte[32];

    private StartupOrchestrator BuildOrchestrator() =>
        new(_log.Object, _databaseService.Object, _appInit.Object,
            _rememberPinService.Object, _startupUi.Object, _scopeFactory.Object,
            _mainFormRunner.Object, _credentialLoader.Object);

    private static Mock<ILifetimeScope> BuildMockScope()
    {
        var scope = new Mock<ILifetimeScope>();
        scope.Setup(s => s.Dispose());
        return scope;
    }

    private void SetupCredentialResult(CredentialLoadResult result) =>
        _credentialLoader.Setup(l => l.LoadAndVerifyCredentials())
            .Returns(result);

    private void SetupNullCredentialResult() =>
        _credentialLoader.Setup(l => l.LoadAndVerifyCredentials())
            .Returns((CredentialLoadResult?)null);

    private Mock<ILifetimeScope> SetupFirstRunScope()
    {
        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<byte[]>()))
            .Returns(ConfigIntegrityResult.FirstRun);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, It.IsAny<AppDatabase>())).Returns(false);
        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);
        return mockScope;
    }

    [Fact]
    public void Run_CredentialLoadCancelled_ReturnsMinus4WithoutCreatingSessionScopeOrMainForm()
    {
        // Arrange
        SetupNullCredentialResult();

        // Act
        var result = BuildOrchestrator().Run(isBackground: false);

        // Assert
        Assert.Equal(-4, result);
        _scopeFactory.Verify(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()), Times.Never);
        _mainFormRunner.Verify(r => r.Run(It.IsAny<ILifetimeScope>(), It.IsAny<Action<ProtectedBuffer, ProtectedBuffer>>()), Times.Never);
        _databaseService.Verify(d => d.SaveCredentialStoreAndConfig(It.IsAny<CredentialStore>(),
            It.IsAny<AppDatabase>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void Run_FirstRun_InitializesNewDatabaseAndSavesAndRunsMainForm()
    {
        // Arrange
        var credResult = new CredentialLoadResult(_store, (byte[])ValidKey32.Clone(), null);
        SetupCredentialResult(credResult);
        var mockScope = SetupFirstRunScope();

        // Act
        var code = BuildOrchestrator().Run(isBackground: false);

        // Assert
        Assert.Equal(0, code);
        _appInit.Verify(a => a.InitializeNewDatabase(It.IsAny<AppDatabase>()), Times.Once);
        _appInit.Verify(a => a.EnsureCurrentAccountCredential(_store, It.IsAny<AppDatabase>()), Times.Once);
        _databaseService.Verify(d => d.SaveCredentialStoreAndConfig(_store, It.IsAny<AppDatabase>(), It.IsAny<byte[]>()), Times.Once);
        _scopeFactory.Verify(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.Is<StartupOptions>(o => !o.IsBackground)), Times.Once);
        _mainFormRunner.Verify(r => r.Run(mockScope.Object, It.IsAny<Action<ProtectedBuffer, ProtectedBuffer>>()), Times.Once);
    }

    [Fact]
    public void Run_ValidConfig_LoadsAndEnsuresCredentialAndAppliesLogVerbosityAndRunsMainForm()
    {
        // Arrange
        var db = new AppDatabase { Settings = new AppSettings { LogVerbosity = LogVerbosity.Warning } };
        var credResult = new CredentialLoadResult(_store, (byte[])ValidKey32.Clone(), null);
        SetupCredentialResult(credResult);

        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<byte[]>()))
            .Returns(ConfigIntegrityResult.Valid);
        _databaseService.Setup(d => d.LoadConfig(It.IsAny<byte[]>())).Returns(db);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, db)).Returns(false);
        _appInit.Setup(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        // Act
        var code = BuildOrchestrator().Run(isBackground: false);

        // Assert
        Assert.Equal(0, code);
        _databaseService.Verify(d => d.LoadConfig(It.IsAny<byte[]>()), Times.Once);
        _appInit.Verify(a => a.EnsureCurrentAccountCredential(_store, db), Times.Once);
        _appInit.Verify(a => a.EnsureInteractiveUserSidName(db), Times.Once);
        _appInit.Verify(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>()), Times.Once);
        _log.VerifySet(l => l.Verbosity = LogVerbosity.Warning, Times.Once);
        _mainFormRunner.Verify(r => r.Run(mockScope.Object, It.IsAny<Action<ProtectedBuffer, ProtectedBuffer>>()), Times.Once);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void Run_ValidConfig_SavesOnlyWhenNormalizationChangedData(
        bool ensureCredChanged, bool normalizeChanged, bool expectSave)
    {
        // Arrange
        var db = new AppDatabase();
        var credResult = new CredentialLoadResult(_store, (byte[])ValidKey32.Clone(), null);
        SetupCredentialResult(credResult);

        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<byte[]>()))
            .Returns(ConfigIntegrityResult.Valid);
        _databaseService.Setup(d => d.LoadConfig(It.IsAny<byte[]>())).Returns(db);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, db)).Returns(ensureCredChanged);
        _appInit.Setup(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>())).Returns(normalizeChanged);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        // Act
        BuildOrchestrator().Run(isBackground: false);

        // Assert
        _databaseService.Verify(d => d.SaveCredentialStoreAndConfig(_store, db, It.IsAny<byte[]>()),
            expectSave ? Times.Once : Times.Never);
    }

    [Fact]
    public void Run_DecryptionFailed_StartFreshAccepted_CreatesNewDatabaseAndRunsMainForm()
    {
        // Arrange
        var credResult = new CredentialLoadResult(_store, (byte[])ValidKey32.Clone(), null);
        SetupCredentialResult(credResult);

        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<byte[]>()))
            .Returns(ConfigIntegrityResult.DecryptionFailed);
        _startupUi.Setup(u => u.ConfirmStartFresh()).Returns(true);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, It.IsAny<AppDatabase>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        // Act
        var code = BuildOrchestrator().Run(isBackground: false);

        // Assert
        Assert.Equal(0, code);
        _appInit.Verify(a => a.InitializeNewDatabase(It.IsAny<AppDatabase>()), Times.Once);
        _databaseService.Verify(d => d.SaveCredentialStoreAndConfig(_store, It.IsAny<AppDatabase>(), It.IsAny<byte[]>()), Times.Once);
        _mainFormRunner.Verify(r => r.Run(mockScope.Object, It.IsAny<Action<ProtectedBuffer, ProtectedBuffer>>()), Times.Once);
    }

    [Fact]
    public void Run_DecryptionFailed_StartFreshRejected_ExitsWithoutSessionScopeOrMainForm()
    {
        // Arrange
        var credResult = new CredentialLoadResult(_store, (byte[])ValidKey32.Clone(), null);
        SetupCredentialResult(credResult);

        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<byte[]>()))
            .Returns(ConfigIntegrityResult.DecryptionFailed);
        _startupUi.Setup(u => u.ConfirmStartFresh()).Returns(false);

        // Act
        var code = BuildOrchestrator().Run(isBackground: false);

        // Assert
        Assert.Equal(0, code);
        _scopeFactory.Verify(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()), Times.Never);
        _mainFormRunner.Verify(r => r.Run(It.IsAny<ILifetimeScope>(), It.IsAny<Action<ProtectedBuffer, ProtectedBuffer>>()), Times.Never);
    }

    [Fact]
    public void Run_SaltMismatch_LoadsWithMismatchKeyReSavesWithCurrentKeyAndRunsCredentialAndSidNameMaintenance()
    {
        // Arrange
        var mismatchKey = new byte[32];
        var db = new AppDatabase();
        var credResult = new CredentialLoadResult(_store, (byte[])ValidKey32.Clone(), mismatchKey);
        SetupCredentialResult(credResult);

        _databaseService.Setup(d => d.LoadConfig(mismatchKey)).Returns(db);
        _appInit.Setup(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>())).Returns(false);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, db)).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        // Act
        var code = BuildOrchestrator().Run(isBackground: false);

        // Assert
        Assert.Equal(0, code);
        _databaseService.Verify(d => d.LoadConfig(mismatchKey), Times.Once);
        _databaseService.Verify(d => d.SaveConfig(db, It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Once);

        // Must NOT call VerifyConfigIntegrity when mismatch load succeeded
        _databaseService.Verify(d => d.VerifyConfigIntegrity(It.IsAny<byte[]>()), Times.Never);

        // Still ensures current account credential and interactive SID name
        _appInit.Verify(a => a.EnsureCurrentAccountCredential(_store, db), Times.Once);
        _appInit.Verify(a => a.EnsureInteractiveUserSidName(db), Times.Once);
    }

    [Fact]
    public void Run_SaltMismatch_MismatchKeyWrongForConfig_FallsThroughToNormalIntegrityCheck()
    {
        // Arrange — mismatch key present but LoadConfig with it throws (wrong key for config)
        var mismatchKey = new byte[32];
        var credResult = new CredentialLoadResult(_store, (byte[])ValidKey32.Clone(), mismatchKey);
        SetupCredentialResult(credResult);

        _databaseService.Setup(d => d.LoadConfig(mismatchKey)).Throws<InvalidOperationException>();
        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<byte[]>()))
            .Returns(ConfigIntegrityResult.FirstRun);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, It.IsAny<AppDatabase>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        // Act
        var code = BuildOrchestrator().Run(isBackground: false);

        // Assert — falls through to FirstRun integrity path
        Assert.Equal(0, code);
        _appInit.Verify(a => a.InitializeNewDatabase(It.IsAny<AppDatabase>()), Times.Once);
    }

    [Fact]
    public void Run_RememberPinResealFailure_LogsDisablesAndContinuesToMainForm()
    {
        // Arrange
        var credResult = new CredentialLoadResult(_store, (byte[])ValidKey32.Clone(), null, PinBypassed: false);
        SetupCredentialResult(credResult);
        SetupFirstRunScope();

        _rememberPinService.Setup(r => r.IsEnabled).Returns(true);
        _rememberPinService.Setup(r => r.UpdateForPinChange(It.IsAny<ProtectedBuffer>()))
            .Throws(new InvalidOperationException("Simulated reseal failure"));

        // Act
        var code = BuildOrchestrator().Run(isBackground: false);

        // Assert
        Assert.Equal(0, code);
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("Failed to refresh Remember PIN key"))), Times.Once);
        _rememberPinService.Verify(r => r.Disable(), Times.Once);
        _mainFormRunner.Verify(r => r.Run(It.IsAny<ILifetimeScope>(), It.IsAny<Action<ProtectedBuffer, ProtectedBuffer>>()), Times.Once);
    }

    [Fact]
    public void Run_RememberPinResealFailure_DisableAlsoFails_LogsBothAndContinues()
    {
        // Arrange
        var credResult = new CredentialLoadResult(_store, (byte[])ValidKey32.Clone(), null, PinBypassed: false);
        SetupCredentialResult(credResult);
        SetupFirstRunScope();

        _rememberPinService.Setup(r => r.IsEnabled).Returns(true);
        _rememberPinService.Setup(r => r.UpdateForPinChange(It.IsAny<ProtectedBuffer>()))
            .Throws(new InvalidOperationException("reseal failure"));
        _rememberPinService.Setup(r => r.Disable())
            .Throws(new InvalidOperationException("cleanup failure"));

        // Act
        var code = BuildOrchestrator().Run(isBackground: false);

        // Assert — both errors logged, startup still proceeds to main form
        Assert.Equal(0, code);
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("Failed to refresh Remember PIN key"))), Times.Once);
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("Failed to clean up Remember PIN key material"))), Times.Once);
        _mainFormRunner.Verify(r => r.Run(It.IsAny<ILifetimeScope>(), It.IsAny<Action<ProtectedBuffer, ProtectedBuffer>>()), Times.Once);
    }

    [Fact]
    public void Run_PinBypassed_SkipsRememberPinResealEvenWhenEnabled()
    {
        // Arrange — PinBypassed = true means key was decrypted via startkey.dat; no reseal needed
        var credResult = new CredentialLoadResult(_store, (byte[])ValidKey32.Clone(), null, PinBypassed: true);
        SetupCredentialResult(credResult);
        SetupFirstRunScope();

        _rememberPinService.Setup(r => r.IsEnabled).Returns(true);

        // Act
        BuildOrchestrator().Run(isBackground: false);

        // Assert
        _rememberPinService.Verify(r => r.UpdateForPinChange(It.IsAny<ProtectedBuffer>()), Times.Never);
    }

    [Fact]
    public void Run_PinDerivedKeyReplaced_DisposesOldBufferAndFinallyDisposesLatestKey()
    {
        // Arrange
        var credResult = new CredentialLoadResult(_store, (byte[])ValidKey32.Clone(), null);
        SetupCredentialResult(credResult);

        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<byte[]>()))
            .Returns(ConfigIntegrityResult.FirstRun);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, It.IsAny<AppDatabase>())).Returns(false);

        var fakeOld = new ProtectedBuffer(new byte[32], protect: false);
        var replacedKey = new ProtectedBuffer(new byte[32], protect: false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        _mainFormRunner
            .Setup(r => r.Run(mockScope.Object, It.IsAny<Action<ProtectedBuffer, ProtectedBuffer>>()))
            .Callback<ILifetimeScope, Action<ProtectedBuffer, ProtectedBuffer>>((_, callback) =>
            {
                // Simulate PinDerivedKeyReplaced: replace the orchestrator's protectedKey
                // with fakeOld in place of its internal key, then swap to replacedKey.
                // We call the callback directly — orchestrator's lambda disposes fakeOld
                // and reassigns protectedKey = replacedKey.
                callback(fakeOld, replacedKey);
            });

        // Act
        var code = BuildOrchestrator().Run(isBackground: false);

        // Assert
        Assert.Equal(0, code);
        // fakeOld was disposed by the callback (oldBuffer.Dispose() in the orchestrator's lambda)
        Assert.Throws<ObjectDisposedException>(() => fakeOld.Unprotect());
        // replacedKey was disposed by the orchestrator's finally block
        Assert.Throws<ObjectDisposedException>(() => replacedKey.Unprotect());
    }

    [Fact]
    public void Run_Background_PassesIsBackgroundTrueToSessionScope()
    {
        // Arrange
        var credResult = new CredentialLoadResult(_store, (byte[])ValidKey32.Clone(), null, PinBypassed: false);
        SetupCredentialResult(credResult);

        StartupOptions? capturedOptions = null;
        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<byte[]>()))
            .Returns(ConfigIntegrityResult.FirstRun);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, It.IsAny<AppDatabase>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Callback<SessionContext, StartupOptions>((_, opts) => capturedOptions = opts)
            .Returns(mockScope.Object);

        // Act
        BuildOrchestrator().Run(isBackground: true);

        // Assert
        Assert.NotNull(capturedOptions);
        Assert.True(capturedOptions.IsBackground);
        Assert.False(capturedOptions.GrantStartupRunAsUnlock);
    }

    [Fact]
    public void Run_BackgroundRunAsStartup_PassesStartupRunAsUnlockGrantToSessionScope()
    {
        var credResult = new CredentialLoadResult(_store, (byte[])ValidKey32.Clone(), null, PinBypassed: false);
        SetupCredentialResult(credResult);

        StartupOptions? capturedOptions = null;
        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<byte[]>()))
            .Returns(ConfigIntegrityResult.FirstRun);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, It.IsAny<AppDatabase>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Callback<SessionContext, StartupOptions>((_, opts) => capturedOptions = opts)
            .Returns(mockScope.Object);

        BuildOrchestrator().Run(isBackground: true, grantStartupRunAsUnlock: true);

        Assert.NotNull(capturedOptions);
        Assert.True(capturedOptions.IsBackground);
        Assert.True(capturedOptions.GrantStartupRunAsUnlock);
    }

    [Fact]
    public void Run_DuplicateSidInCredentialStore_LogsWarning()
    {
        // Arrange — two credentials with the same SID
        _store.Credentials.Add(new CredentialEntry { Sid = "S-1-5-21-100-200-300-1001", EncryptedPassword = [] });
        _store.Credentials.Add(new CredentialEntry { Sid = "S-1-5-21-100-200-300-1001", EncryptedPassword = [] });

        var credResult = new CredentialLoadResult(_store, (byte[])ValidKey32.Clone(), null);
        SetupCredentialResult(credResult);
        SetupFirstRunScope();

        // Act
        BuildOrchestrator().Run(isBackground: false);

        // Assert
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("Duplicate credential SID detected"))), Times.Once);
    }

    [Fact]
    public void Run_SessionScopeDisposedAfterMainFormRunnerReturns()
    {
        // Arrange
        var credResult = new CredentialLoadResult(_store, (byte[])ValidKey32.Clone(), null);
        SetupCredentialResult(credResult);
        var mockScope = SetupFirstRunScope();

        // Act
        BuildOrchestrator().Run(isBackground: false);

        // Assert — ILifetimeScope.Dispose is called via the using statement
        mockScope.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact]
    public void Run_SaltMismatch_NormalizeAccountSidsCalledDuringMismatchLoad()
    {
        // Arrange — mismatch key present; NormalizeAccountSids must be called on the mismatch-loaded database
        var mismatchKey = new byte[32];
        var db = new AppDatabase();
        var credResult = new CredentialLoadResult(_store, (byte[])ValidKey32.Clone(), mismatchKey);
        SetupCredentialResult(credResult);

        _databaseService.Setup(d => d.LoadConfig(mismatchKey)).Returns(db);
        _appInit.Setup(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>())).Returns(false);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, db)).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        // Act
        BuildOrchestrator().Run(isBackground: false);

        // Assert — NormalizeAccountSids is called on the mismatch-loaded database before re-saving
        _appInit.Verify(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>()), Times.Once);
        _databaseService.Verify(d => d.SaveConfig(db, It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public void Run_SaltMismatch_EnsureCredentialChangedAfterMismatchLoad_SavesCredentialStoreAndConfig()
    {
        // Arrange — mismatch key path; EnsureCurrentAccountCredential returns true (credential added)
        var mismatchKey = new byte[32];
        var db = new AppDatabase();
        var credResult = new CredentialLoadResult(_store, (byte[])ValidKey32.Clone(), mismatchKey);
        SetupCredentialResult(credResult);

        _databaseService.Setup(d => d.LoadConfig(mismatchKey)).Returns(db);
        _appInit.Setup(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>())).Returns(false);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, db)).Returns(true);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        // Act
        BuildOrchestrator().Run(isBackground: false);

        // Assert — credential change triggers SaveCredentialStoreAndConfig (not just SaveConfig)
        _databaseService.Verify(d => d.SaveCredentialStoreAndConfig(_store, db, It.IsAny<byte[]>()), Times.Once);
    }
}
