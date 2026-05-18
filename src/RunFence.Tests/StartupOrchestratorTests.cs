using Autofac;
using Moq;
using System.Security.Cryptography;
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
    private readonly Mock<IConfigPaths> _configPaths = new();
    private readonly Mock<ILoadedGoodBackupStore> _loadedGoodBackupStore = new();
    private readonly Mock<IAppInitializationHelper> _appInit = new();
    private readonly Mock<IRememberPinService> _rememberPinService = new();
    private readonly Mock<IPinService> _pinService = new();
    private readonly Mock<IStartupUI> _startupUi = new();
    private readonly Mock<IStartupSessionScopeFactory> _scopeFactory = new();
    private readonly Mock<IStartupMainFormRunner> _mainFormRunner = new();
    private readonly Mock<IStartupCredentialLoader> _credentialLoader = new();

    private readonly CredentialStore _store = new();

    private static readonly byte[] ValidKey32 = new byte[32];

    public StartupOrchestratorTests()
    {
        _configPaths.SetupGet(p => p.ConfigFilePath).Returns(@"C:\RunFence\config.dat");
        _configPaths.SetupGet(p => p.CredentialsFilePath).Returns(@"C:\RunFence\credentials.dat");
        _loadedGoodBackupStore
            .Setup(s => s.GetBackupPath(_configPaths.Object.ConfigFilePath))
            .Returns(_configPaths.Object.ConfigFilePath + ".lastgood");
        string? ignoredWarning = null;
        _loadedGoodBackupStore
            .Setup(s => s.TryPreserveCurrentFile(It.IsAny<string>(), out ignoredWarning))
            .Returns(true);
        _databaseService.Setup(d => d.TryGetConfigSaltFromPath(It.IsAny<string>())).Returns((byte[]?)null);
    }

    private StartupOrchestrator BuildOrchestrator() =>
        new(_log.Object, _databaseService.Object, _configPaths.Object, _loadedGoodBackupStore.Object, _appInit.Object,
            _rememberPinService.Object, new ConfigMismatchPinVerifier(_pinService.Object), _startupUi.Object, _scopeFactory.Object,
            _mainFormRunner.Object, _credentialLoader.Object);

    private static SecureSecret CreateSecret(byte[] bytes)
        => new(bytes.Length, data => bytes.AsSpan().CopyTo(data));

    private static Mock<ILifetimeScope> BuildMockScope()
    {
        var scope = new Mock<ILifetimeScope>();
        scope.Setup(s => s.Dispose());
        return scope;
    }

    private CredentialLoadResult CreateCredentialResult(byte[] key, byte[]? mismatchKey = null, bool pinBypassed = false)
        => new(_store, CreateSecret(key), mismatchKey == null ? null : CreateSecret(mismatchKey), pinBypassed);

    private void SetupCredentialResult(CredentialLoadResult result) =>
        _credentialLoader.Setup(l => l.LoadAndVerifyCredentials(_configPaths.Object.ConfigFilePath))
            .Returns(result);

    private static byte[] SnapshotSecret(ISecureSecretSnapshotSource source)
        => source.TransformSnapshot(data => data.ToArray());

    private void SetupNullCredentialResult() =>
        _credentialLoader.Setup(l => l.LoadAndVerifyCredentials(_configPaths.Object.ConfigFilePath))
            .Returns((CredentialLoadResult?)null);

    private Mock<ILifetimeScope> SetupFirstRunScope()
    {
        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<ISecureSecretSnapshotSource>()))
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
        SetupNullCredentialResult();

        var result = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(-4, result);
        _scopeFactory.Verify(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()), Times.Never);
        _mainFormRunner.Verify(r => r.Run(It.IsAny<ILifetimeScope>()), Times.Never);
        _databaseService.Verify(d => d.SaveCredentialStoreAndConfig(It.IsAny<CredentialStore>(),
            It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
    }

    [Fact]
    public void Run_FirstRun_InitializesNewDatabaseAndSavesAndRunsMainForm()
    {
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        SetupCredentialResult(credResult);
        var mockScope = SetupFirstRunScope();

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        _appInit.Verify(a => a.InitializeNewDatabase(It.IsAny<AppDatabase>()), Times.Once);
        _appInit.Verify(a => a.EnsureCurrentAccountCredential(_store, It.IsAny<AppDatabase>()), Times.Once);
        _databaseService.Verify(d => d.SaveCredentialStoreAndConfig(_store, It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()), Times.Once);
        _scopeFactory.Verify(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.Is<StartupOptions>(o => !o.IsBackground)), Times.Once);
        _mainFormRunner.Verify(r => r.Run(mockScope.Object), Times.Once);
    }

    [Fact]
    public void Run_TransfersStartupKeyIntoSessionBeforeMainForm()
    {
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        SetupCredentialResult(credResult);
        SessionContext? capturedSession = null;

        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(ConfigIntegrityResult.FirstRun);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, It.IsAny<AppDatabase>())).Returns(false);
        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Callback<SessionContext, StartupOptions>((session, _) => capturedSession = session)
            .Returns(mockScope.Object);

        byte[]? observedKey = null;
        _mainFormRunner.Setup(r => r.Run(mockScope.Object))
            .Callback(() =>
            {
                observedKey = capturedSession!.PinDerivedKey.TransformSnapshot(data => data.ToArray());
            });

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        Assert.NotNull(observedKey);
        Assert.Equal(ValidKey32, observedKey);
    }

    [Fact]
    public void Run_ValidConfig_LoadsAndEnsuresCredentialAndAppliesLogVerbosityAndRunsMainForm()
    {
        var db = new AppDatabase { Settings = new AppSettings { LogVerbosity = LogVerbosity.Warning } };
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        SetupCredentialResult(credResult);

        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(ConfigIntegrityResult.Valid);
        _databaseService.Setup(d => d.LoadConfig(It.IsAny<ISecureSecretSnapshotSource>())).Returns(db);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, db)).Returns(false);
        _appInit.Setup(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        _databaseService.Verify(d => d.LoadConfig(It.IsAny<ISecureSecretSnapshotSource>()), Times.Once);
        _appInit.Verify(a => a.EnsureCurrentAccountCredential(_store, db), Times.Once);
        _appInit.Verify(a => a.EnsureInteractiveUserSidName(db), Times.Once);
        _appInit.Verify(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>()), Times.Once);
        _loadedGoodBackupStore.Verify(
            s => s.TryPreserveCurrentFile(_configPaths.Object.CredentialsFilePath, out It.Ref<string?>.IsAny),
            Times.Once);
        _loadedGoodBackupStore.Verify(
            s => s.TryPreserveCurrentFile(_configPaths.Object.ConfigFilePath, out It.Ref<string?>.IsAny),
            Times.Once);
        _log.VerifySet(l => l.Verbosity = LogVerbosity.Warning, Times.Once);
        _mainFormRunner.Verify(r => r.Run(mockScope.Object), Times.Once);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void Run_ValidConfig_SavesOnlyWhenNormalizationChangedData(
        bool ensureCredChanged, bool normalizeChanged, bool expectSave)
    {
        var db = new AppDatabase();
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        SetupCredentialResult(credResult);

        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(ConfigIntegrityResult.Valid);
        _databaseService.Setup(d => d.LoadConfig(It.IsAny<ISecureSecretSnapshotSource>())).Returns(db);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, db)).Returns(ensureCredChanged);
        _appInit.Setup(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>())).Returns(normalizeChanged);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        BuildOrchestrator().Run(isBackground: false);

        _databaseService.Verify(d => d.SaveCredentialStoreAndConfig(_store, db, It.IsAny<ISecureSecretSnapshotSource>()),
            expectSave ? Times.Once : Times.Never);
    }

    [Fact]
    public void Run_DecryptionFailed_StartFreshAccepted_CreatesNewDatabaseAndRunsMainForm()
    {
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        SetupCredentialResult(credResult);

        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(ConfigIntegrityResult.DecryptionFailed);
        _startupUi.Setup(u => u.ConfirmStartFresh(false)).Returns(StartupConfigRecoveryChoice.StartFresh);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, It.IsAny<AppDatabase>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        _appInit.Verify(a => a.InitializeNewDatabase(It.IsAny<AppDatabase>()), Times.Once);
        _databaseService.Verify(d => d.SaveCredentialStoreAndConfig(_store, It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()), Times.Once);
        _mainFormRunner.Verify(r => r.Run(mockScope.Object), Times.Once);
    }

    [Fact]
    public void Run_DecryptionFailed_StartFreshRejected_ExitsWithoutSessionScopeOrMainForm()
    {
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        SetupCredentialResult(credResult);

        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(ConfigIntegrityResult.DecryptionFailed);
        _startupUi.Setup(u => u.ConfirmStartFresh(false)).Returns(StartupConfigRecoveryChoice.Exit);

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        _scopeFactory.Verify(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()), Times.Never);
        _mainFormRunner.Verify(r => r.Run(It.IsAny<ILifetimeScope>()), Times.Never);
    }

    [Fact]
    public void Run_DecryptionFailed_UserChoosesBackup_LoadsBackupDirectlyAndRunsMainForm()
    {
        var db = new AppDatabase();
        using var firstCredResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        using var secondCredResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        _credentialLoader.SetupSequence(l => l.LoadAndVerifyCredentials(It.IsAny<string>()))
            .Returns(firstCredResult)
            .Returns(secondCredResult);

        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(ConfigIntegrityResult.DecryptionFailed);
        _loadedGoodBackupStore.Setup(s => s.Exists(_configPaths.Object.ConfigFilePath)).Returns(true);
        _startupUi.Setup(u => u.ConfirmStartFresh(true)).Returns(StartupConfigRecoveryChoice.UseBackup);
        _databaseService
            .Setup(d => d.LoadConfigFromPath(_configPaths.Object.ConfigFilePath + ".lastgood", It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(db);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, db)).Returns(false);
        _appInit.Setup(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        _databaseService.Verify(d => d.LoadConfigFromPath(
            _configPaths.Object.ConfigFilePath + ".lastgood",
            It.IsAny<ISecureSecretSnapshotSource>()), Times.Once);
        _databaseService.Verify(d => d.SaveConfig(db, It.IsAny<ISecureSecretSnapshotSource>(), _store.ArgonSalt), Times.Once);
        _databaseService.Verify(d => d.SaveCredentialStoreAndConfig(It.IsAny<CredentialStore>(), It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
        _loadedGoodBackupStore.Verify(d => d.Restore(It.IsAny<string>()), Times.Never);
        _loadedGoodBackupStore.Verify(
            d => d.TryPreserveCurrentFile(_configPaths.Object.CredentialsFilePath, out It.Ref<string?>.IsAny),
            Times.Once);
        _loadedGoodBackupStore.Verify(
            d => d.TryPreserveCurrentFile(_configPaths.Object.ConfigFilePath, out It.Ref<string?>.IsAny),
            Times.Once);
        _loadedGoodBackupStore.Verify(
            d => d.TryPreserveCurrentFile(_configPaths.Object.ConfigFilePath + ".lastgood", out It.Ref<string?>.IsAny),
            Times.Never);
        _appInit.Verify(a => a.InitializeNewDatabase(It.IsAny<AppDatabase>()), Times.Never);
        _mainFormRunner.Verify(r => r.Run(mockScope.Object), Times.Once);
    }

    [Fact]
    public void Run_BackupSourceLoad_WhenEnsureCurrentAccountCredentialChangesData_UsesCombinedSaveOnly()
    {
        var db = new AppDatabase();
        using var firstCredResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        using var secondCredResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        _credentialLoader.SetupSequence(l => l.LoadAndVerifyCredentials(It.IsAny<string>()))
            .Returns(firstCredResult)
            .Returns(secondCredResult);

        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(ConfigIntegrityResult.DecryptionFailed);
        _loadedGoodBackupStore.Setup(s => s.Exists(_configPaths.Object.ConfigFilePath)).Returns(true);
        _startupUi.Setup(u => u.ConfirmStartFresh(true)).Returns(StartupConfigRecoveryChoice.UseBackup);
        _databaseService
            .Setup(d => d.LoadConfigFromPath(_configPaths.Object.ConfigFilePath + ".lastgood", It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(db);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, db)).Returns(true);
        _appInit.Setup(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        _databaseService.Verify(d => d.SaveCredentialStoreAndConfig(_store, db, It.IsAny<ISecureSecretSnapshotSource>()), Times.Once);
        _databaseService.Verify(d => d.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void Run_BackupSourceLoad_WhenNormalizeAccountSidsChangesData_UsesCombinedSaveOnly()
    {
        var db = new AppDatabase();
        using var firstCredResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        using var secondCredResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        _credentialLoader.SetupSequence(l => l.LoadAndVerifyCredentials(It.IsAny<string>()))
            .Returns(firstCredResult)
            .Returns(secondCredResult);

        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(ConfigIntegrityResult.DecryptionFailed);
        _loadedGoodBackupStore.Setup(s => s.Exists(_configPaths.Object.ConfigFilePath)).Returns(true);
        _startupUi.Setup(u => u.ConfirmStartFresh(true)).Returns(StartupConfigRecoveryChoice.UseBackup);
        _databaseService
            .Setup(d => d.LoadConfigFromPath(_configPaths.Object.ConfigFilePath + ".lastgood", It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(db);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, db)).Returns(false);
        _appInit.Setup(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>())).Returns(true);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        _databaseService.Verify(d => d.SaveCredentialStoreAndConfig(_store, db, It.IsAny<ISecureSecretSnapshotSource>()), Times.Once);
        _databaseService.Verify(d => d.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void Run_FirstRun_UserChoosesBackup_MissingBackupSource_StartsFreshAndRunsMainForm()
    {
        using var firstCredResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        using var secondCredResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        _credentialLoader.SetupSequence(l => l.LoadAndVerifyCredentials(It.IsAny<string>()))
            .Returns(firstCredResult)
            .Returns(secondCredResult);

        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(ConfigIntegrityResult.FirstRun);
        _loadedGoodBackupStore.Setup(s => s.Exists(_configPaths.Object.ConfigFilePath)).Returns(true);
        _databaseService
            .Setup(d => d.LoadConfigFromPath(_configPaths.Object.ConfigFilePath + ".lastgood", It.IsAny<ISecureSecretSnapshotSource>()))
            .Throws(new FileNotFoundException());
        _startupUi.SetupSequence(u => u.ConfirmStartFresh(It.IsAny<bool>()))
            .Returns(StartupConfigRecoveryChoice.UseBackup)
            .Returns(StartupConfigRecoveryChoice.StartFresh);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, It.IsAny<AppDatabase>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        _databaseService.Verify(d => d.LoadConfig(It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
        _appInit.Verify(a => a.InitializeNewDatabase(It.IsAny<AppDatabase>()), Times.Once);
        _databaseService.Verify(d => d.SaveCredentialStoreAndConfig(_store, It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()), Times.Once);
        _mainFormRunner.Verify(r => r.Run(mockScope.Object), Times.Once);
    }

    [Fact]
    public void Run_DecryptionFailed_UserChoosesBackup_MissingBackupSource_StartsFreshAndRunsMainForm()
    {
        using var firstCredResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        using var secondCredResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        _credentialLoader.SetupSequence(l => l.LoadAndVerifyCredentials(It.IsAny<string>()))
            .Returns(firstCredResult)
            .Returns(secondCredResult);

        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(ConfigIntegrityResult.DecryptionFailed);
        _loadedGoodBackupStore.Setup(s => s.Exists(_configPaths.Object.ConfigFilePath)).Returns(true);
        _databaseService
            .Setup(d => d.LoadConfigFromPath(_configPaths.Object.ConfigFilePath + ".lastgood", It.IsAny<ISecureSecretSnapshotSource>()))
            .Throws(new FileNotFoundException());
        _startupUi.SetupSequence(u => u.ConfirmStartFresh(It.IsAny<bool>()))
            .Returns(StartupConfigRecoveryChoice.UseBackup)
            .Returns(StartupConfigRecoveryChoice.StartFresh);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, It.IsAny<AppDatabase>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        _databaseService.Verify(d => d.LoadConfig(It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
        _appInit.Verify(a => a.InitializeNewDatabase(It.IsAny<AppDatabase>()), Times.Once);
        _databaseService.Verify(d => d.SaveCredentialStoreAndConfig(_store, It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()), Times.Once);
        _mainFormRunner.Verify(r => r.Run(mockScope.Object), Times.Once);
    }

    [Fact]
    public void Run_SaltMismatch_LoadsWithMismatchKeyReSavesWithCurrentKeyAndRunsCredentialAndSidNameMaintenance()
    {
        var mismatchKey = Enumerable.Repeat((byte)0xF0, 32).ToArray();
        var db = new AppDatabase();
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone(), mismatchKey);
        SetupCredentialResult(credResult);
        var loadSnapshots = new List<byte[]>();
        byte[]? saveSnapshot = null;
        byte[]? saveArgonSalt = null;

        _databaseService.Setup(d => d.LoadConfig(It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback<ISecureSecretSnapshotSource>(snapshot => loadSnapshots.Add(SnapshotSecret(snapshot)))
            .Returns(db);
        _databaseService.Setup(d => d.SaveConfig(
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()))
            .Callback<AppDatabase, ISecureSecretSnapshotSource, byte[]>((loadedDb, snapshot, argonSalt) =>
            {
                if (!ReferenceEquals(loadedDb, db))
                    return;

                saveSnapshot = SnapshotSecret(snapshot);
                saveArgonSalt = (byte[])argonSalt.Clone();
            });
        _appInit.Setup(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>())).Returns(false);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, db)).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        Assert.Single(loadSnapshots);
        Assert.Equal(mismatchKey, loadSnapshots[0]);
        Assert.Equal((byte[])ValidKey32.Clone(), saveSnapshot);
        Assert.Equal(_store.ArgonSalt, saveArgonSalt);
        _databaseService.Verify(d => d.VerifyConfigIntegrity(It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
        _appInit.Verify(a => a.EnsureCurrentAccountCredential(_store, db), Times.Once);
        _appInit.Verify(a => a.EnsureInteractiveUserSidName(db), Times.Once);
    }

    [Fact]
    public void Run_SaltMismatch_MismatchKeyWrongForConfig_UsesRecoveryFlow()
    {
        var mismatchKey = Enumerable.Repeat((byte)0xA1, 32).ToArray();
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone(), mismatchKey);
        SetupCredentialResult(credResult);
        var loadSnapshots = new List<byte[]>();

        _databaseService.Setup(d => d.LoadConfig(It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback<ISecureSecretSnapshotSource>(snapshot => loadSnapshots.Add(SnapshotSecret(snapshot)))
            .Throws(new CryptographicException("wrong key"));
        _startupUi.Setup(u => u.ConfirmStartFresh(false)).Returns(StartupConfigRecoveryChoice.StartFresh);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, It.IsAny<AppDatabase>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        Assert.Single(loadSnapshots);
        Assert.Equal(mismatchKey, loadSnapshots[0]);
        _startupUi.Verify(u => u.PromptMainConfigMismatchPin(It.IsAny<string>(), It.IsAny<Func<ProtectedString, MainConfigPinVerificationResult>>()), Times.Never);
        _appInit.Verify(a => a.InitializeNewDatabase(It.IsAny<AppDatabase>()), Times.Once);
    }

    [Fact]
    public void Run_SaltMismatch_MismatchKeyLoadThrowsFatalError_UsesRecoveryFlow()
    {
        var mismatchKey = Enumerable.Repeat((byte)0xA2, 32).ToArray();
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone(), mismatchKey);
        SetupCredentialResult(credResult);
        var loadSnapshots = new List<byte[]>();

        _databaseService.Setup(d => d.LoadConfig(It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback<ISecureSecretSnapshotSource>(snapshot => loadSnapshots.Add(SnapshotSecret(snapshot)))
            .Throws(new InvalidOperationException("hard read failure"));
        _startupUi.Setup(u => u.ConfirmStartFresh(false)).Returns(StartupConfigRecoveryChoice.StartFresh);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, It.IsAny<AppDatabase>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        Assert.Single(loadSnapshots);
        Assert.Equal(mismatchKey, loadSnapshots[0]);
        _startupUi.Verify(u => u.PromptMainConfigMismatchPin(It.IsAny<string>(), It.IsAny<Func<ProtectedString, MainConfigPinVerificationResult>>()), Times.Never);
        _appInit.Verify(a => a.InitializeNewDatabase(It.IsAny<AppDatabase>()), Times.Once);
    }

    [Fact]
    public void Run_SaltMismatch_LoadsWithMismatchKeyAndEnsureCurrentAccountCredentialChanged_UsesCombinedSaveOnly()
    {
        var mismatchKey = Enumerable.Repeat((byte)0xB0, 32).ToArray();
        var db = new AppDatabase();
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone(), mismatchKey);
        SetupCredentialResult(credResult);
        var loadSnapshots = new List<byte[]>();
        var saveCredentialStoreCalls = 0;
        byte[]? saveSnapshot = null;

        _databaseService.Setup(d => d.LoadConfig(It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback<ISecureSecretSnapshotSource>(snapshot => loadSnapshots.Add(SnapshotSecret(snapshot)))
            .Returns(db);
        _databaseService.Setup(d => d.SaveCredentialStoreAndConfig(It.IsAny<CredentialStore>(), It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback<CredentialStore, AppDatabase, ISecureSecretSnapshotSource>((store, loadedDb, snapshot) =>
            {
                if (!ReferenceEquals(store, _store) || !ReferenceEquals(loadedDb, db))
                    return;

                saveCredentialStoreCalls++;
                saveSnapshot = SnapshotSecret(snapshot);
            });
        var saveConfigCalled = false;
        _databaseService.Setup(d => d.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
            .Callback<AppDatabase, ISecureSecretSnapshotSource, byte[]>((_, _, _) => saveConfigCalled = true);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, db)).Returns(true);
        _appInit.Setup(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        Assert.Single(loadSnapshots);
        Assert.Equal(mismatchKey, loadSnapshots[0]);
        Assert.Equal(1, saveCredentialStoreCalls);
        Assert.Equal((byte[])ValidKey32.Clone(), saveSnapshot);
        Assert.False(saveConfigCalled);
        _appInit.Verify(a => a.EnsureCurrentAccountCredential(_store, db), Times.Once);
    }

    [Fact]
    public void Run_SaltMismatch_LoadsWithMismatchKeyAndNormalizeAccountSidsChanged_UsesCombinedSaveOnly()
    {
        var mismatchKey = Enumerable.Repeat((byte)0xB1, 32).ToArray();
        var db = new AppDatabase();
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone(), mismatchKey);
        SetupCredentialResult(credResult);
        var loadSnapshots = new List<byte[]>();
        var saveCredentialStoreCalls = 0;
        byte[]? saveSnapshot = null;

        _databaseService.Setup(d => d.LoadConfig(It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback<ISecureSecretSnapshotSource>(snapshot => loadSnapshots.Add(SnapshotSecret(snapshot)))
            .Returns(db);
        _databaseService.Setup(d => d.SaveCredentialStoreAndConfig(It.IsAny<CredentialStore>(), It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback<CredentialStore, AppDatabase, ISecureSecretSnapshotSource>((store, loadedDb, snapshot) =>
            {
                if (!ReferenceEquals(store, _store) || !ReferenceEquals(loadedDb, db))
                    return;

                saveCredentialStoreCalls++;
                saveSnapshot = SnapshotSecret(snapshot);
            });
        var saveConfigCalled = false;
        _databaseService.Setup(d => d.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
            .Callback<AppDatabase, ISecureSecretSnapshotSource, byte[]>((_, _, _) => saveConfigCalled = true);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, db)).Returns(false);
        _appInit.Setup(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>())).Returns(true);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        Assert.Single(loadSnapshots);
        Assert.Equal(mismatchKey, loadSnapshots[0]);
        Assert.Equal(1, saveCredentialStoreCalls);
        Assert.Equal((byte[])ValidKey32.Clone(), saveSnapshot);
        Assert.False(saveConfigCalled);
        _appInit.Verify(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void Run_SaltMismatch_LoadsFromBackupWithMismatchKeyLoadSourceAndSavesWithCurrentKey()
    {
        var mismatchKey = Enumerable.Repeat((byte)0xC0, 32).ToArray();
        var selectedConfigSalt = Enumerable.Repeat((byte)0x33, 32).ToArray();
        var db = new AppDatabase();
        var backupPath = _configPaths.Object.ConfigFilePath + ".lastgood";
        using var firstCredResult = CreateCredentialResult((byte[])ValidKey32.Clone(), mismatchKey);
        using var secondCredResult = CreateCredentialResult((byte[])ValidKey32.Clone(), mismatchKey);
        _credentialLoader.SetupSequence(l => l.LoadAndVerifyCredentials(It.IsAny<string>()))
            .Returns(firstCredResult)
            .Returns(secondCredResult);

        _databaseService.Setup(d => d.TryGetConfigSaltFromPath(_configPaths.Object.ConfigFilePath))
            .Returns(selectedConfigSalt);
        _databaseService.Setup(d => d.TryGetConfigSaltFromPath(backupPath))
            .Returns(selectedConfigSalt);

        byte[]? saveCredentialStoreSnapshot = null;
        var saveCredentialStoreCalls = 0;
        var loadSnapshots = new List<byte[]>();

        _databaseService.Setup(d => d.LoadConfig(It.IsAny<ISecureSecretSnapshotSource>()))
            .Throws(new CryptographicException("primary unavailable"));
        _databaseService.Setup(d => d.LoadConfigFromPath(backupPath, It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback<string, ISecureSecretSnapshotSource>((_, snapshot) => loadSnapshots.Add(SnapshotSecret(snapshot)))
            .Returns(db);
        _databaseService.Setup(d => d.SaveCredentialStoreAndConfig(It.IsAny<CredentialStore>(), It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback<CredentialStore, AppDatabase, ISecureSecretSnapshotSource>((_, _, snapshot) =>
            {
                saveCredentialStoreCalls++;
                saveCredentialStoreSnapshot = SnapshotSecret(snapshot);
            });

        _loadedGoodBackupStore.Setup(s => s.GetBackupPath(_configPaths.Object.ConfigFilePath))
            .Returns(backupPath);
        _loadedGoodBackupStore.Setup(s => s.Exists(_configPaths.Object.ConfigFilePath)).Returns(true);
        _startupUi.Setup(u => u.ConfirmStartFresh(true)).Returns(StartupConfigRecoveryChoice.UseBackup);
        _startupUi
            .Setup(u => u.PromptMainConfigMismatchPin(
                It.IsAny<string>(),
                It.IsAny<Func<ProtectedString, MainConfigPinVerificationResult>>()))
            .Returns(MainConfigPinPromptResult.AbortToRecovery);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, db)).Returns(true);
        _appInit.Setup(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        Assert.Single(loadSnapshots);
        Assert.Equal(mismatchKey, loadSnapshots[0]);
        Assert.Equal(1, saveCredentialStoreCalls);
        Assert.Equal((byte[])ValidKey32.Clone(), saveCredentialStoreSnapshot);
        _databaseService.Verify(d => d.SaveConfig(It.IsAny<AppDatabase>(), It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void Run_ConfigDecryptionFailed_ConfigPinVerifierCapturesDatabaseAndDoesNotLoadAgain()
    {
        var selectedConfigSalt = Enumerable.Repeat((byte)0x42, 32).ToArray();
        var selectedConfigKey = Enumerable.Repeat((byte)0x24, 32).ToArray();
        var db = new AppDatabase();
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        SetupCredentialResult(credResult);
        var loadSnapshots = new List<byte[]>();
        byte[]? saveSnapshot = null;
        byte[]? saveArgonSalt = null;

        _databaseService.Setup(d => d.TryGetConfigSaltFromPath(_configPaths.Object.ConfigFilePath))
            .Returns(selectedConfigSalt);
        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(ConfigIntegrityResult.DecryptionFailed);
        _pinService.Setup(s => s.DeriveKeySecret(It.IsAny<ProtectedString>(), selectedConfigSalt))
            .Returns(() => CreateSecret(selectedConfigKey));
        _databaseService.Setup(d => d.LoadConfig(It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback<ISecureSecretSnapshotSource>(snapshot => loadSnapshots.Add(SnapshotSecret(snapshot)))
            .Returns(db);
        _databaseService.Setup(d => d.SaveConfig(
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()))
            .Callback<AppDatabase, ISecureSecretSnapshotSource, byte[]>((loadedDb, snapshot, argonSalt) =>
            {
                if (!ReferenceEquals(loadedDb, db))
                    return;

                saveSnapshot = SnapshotSecret(snapshot);
                saveArgonSalt = (byte[])argonSalt.Clone();
            });
        _startupUi
            .Setup(u => u.PromptMainConfigMismatchPin(
                _configPaths.Object.ConfigFilePath,
                It.IsAny<Func<ProtectedString, MainConfigPinVerificationResult>>()))
            .Returns<string, Func<ProtectedString, MainConfigPinVerificationResult>>((_, verifyPin) =>
            {
                using var pin = ProtectedString.FromChars("1234".AsSpan());
                Assert.Equal(MainConfigPinVerificationResult.Verified, verifyPin(pin));
                return MainConfigPinPromptResult.Verified;
            });
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, db)).Returns(false);
        _appInit.Setup(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        Assert.Single(loadSnapshots);
        Assert.Equal(selectedConfigKey, loadSnapshots[0]);
        Assert.Equal((byte[])ValidKey32.Clone(), saveSnapshot);
        Assert.Equal(_store.ArgonSalt, saveArgonSalt);
        _startupUi.Verify(u => u.ConfirmStartFresh(It.IsAny<bool>()), Times.Never);
        _mainFormRunner.Verify(r => r.Run(mockScope.Object), Times.Once);
    }

    [Fact]
    public void Run_ConfigDecryptionFailed_WrongSelectedConfigPin_RepromptsUntilCorrect()
    {
        var selectedConfigSalt = Enumerable.Repeat((byte)0x42, 32).ToArray();
        var selectedConfigKey = Enumerable.Repeat((byte)0x24, 32).ToArray();
        var db = new AppDatabase();
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        SetupCredentialResult(credResult);
        var loadSnapshots = new List<byte[]>();
        var loadAttempts = 0;
        byte[]? saveSnapshot = null;
        byte[]? saveArgonSalt = null;

        _databaseService.Setup(d => d.TryGetConfigSaltFromPath(_configPaths.Object.ConfigFilePath))
            .Returns(selectedConfigSalt);
        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(ConfigIntegrityResult.DecryptionFailed);
        _pinService.Setup(s => s.DeriveKeySecret(It.IsAny<ProtectedString>(), selectedConfigSalt))
            .Returns(() => CreateSecret(selectedConfigKey));
        _databaseService
            .Setup(d => d.LoadConfig(It.IsAny<ISecureSecretSnapshotSource>()))
            .Callback<ISecureSecretSnapshotSource>(snapshot => loadSnapshots.Add(SnapshotSecret(snapshot)))
            .Returns(() =>
            {
                loadAttempts++;
                if (loadAttempts == 1)
                    throw new CryptographicException("wrong pin");

                return db;
            });
        _databaseService.Setup(d => d.SaveConfig(
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()))
            .Callback<AppDatabase, ISecureSecretSnapshotSource, byte[]>((loadedDb, snapshot, argonSalt) =>
            {
                if (!ReferenceEquals(loadedDb, db))
                    return;

                saveSnapshot = SnapshotSecret(snapshot);
                saveArgonSalt = (byte[])argonSalt.Clone();
            });
        _startupUi
            .Setup(u => u.PromptMainConfigMismatchPin(
                _configPaths.Object.ConfigFilePath,
                It.IsAny<Func<ProtectedString, MainConfigPinVerificationResult>>()))
            .Returns<string, Func<ProtectedString, MainConfigPinVerificationResult>>((_, verifyPin) =>
            {
                using var wrongPin = ProtectedString.FromChars("1111".AsSpan());
                Assert.Equal(MainConfigPinVerificationResult.WrongPin, verifyPin(wrongPin));

                using var correctPin = ProtectedString.FromChars("1234".AsSpan());
                Assert.Equal(MainConfigPinVerificationResult.Verified, verifyPin(correctPin));
                return MainConfigPinPromptResult.Verified;
            });
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, db)).Returns(false);
        _appInit.Setup(a => a.NormalizeAccountSids(db.Apps, It.IsAny<string>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        Assert.Equal(2, loadSnapshots.Count);
        Assert.All(loadSnapshots, snapshot => Assert.Equal(selectedConfigKey, snapshot));
        Assert.Equal((byte[])ValidKey32.Clone(), saveSnapshot);
        Assert.Equal(_store.ArgonSalt, saveArgonSalt);
        _startupUi.Verify(u => u.ConfirmStartFresh(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void Run_ConfigDecryptionFailed_FatalSelectedConfigVerification_UsesRecoveryFlow()
    {
        var selectedConfigSalt = Enumerable.Repeat((byte)0x42, 32).ToArray();
        var fatal = new InvalidOperationException("broken selected config load");
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        SetupCredentialResult(credResult);

        _databaseService.Setup(d => d.TryGetConfigSaltFromPath(_configPaths.Object.ConfigFilePath))
            .Returns(selectedConfigSalt);
        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(ConfigIntegrityResult.DecryptionFailed);
        _pinService.Setup(s => s.DeriveKeySecret(It.IsAny<ProtectedString>(), selectedConfigSalt))
            .Returns(() => CreateSecret(Enumerable.Repeat((byte)0x24, 32).ToArray()));
        _databaseService.Setup(d => d.LoadConfig(It.IsAny<ISecureSecretSnapshotSource>()))
            .Throws(fatal);
        _startupUi
            .Setup(u => u.PromptMainConfigMismatchPin(
                _configPaths.Object.ConfigFilePath,
                It.IsAny<Func<ProtectedString, MainConfigPinVerificationResult>>()))
            .Returns<string, Func<ProtectedString, MainConfigPinVerificationResult>>((_, verifyPin) =>
            {
                using var pin = ProtectedString.FromChars("1234".AsSpan());
                Assert.Equal(MainConfigPinVerificationResult.AbortToRecovery, verifyPin(pin));
                return MainConfigPinPromptResult.AbortToRecovery;
            });
        _startupUi.Setup(u => u.ConfirmStartFresh(false)).Returns(StartupConfigRecoveryChoice.StartFresh);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, It.IsAny<AppDatabase>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Returns(mockScope.Object);

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        _log.Verify(
            l => l.Error(
                It.Is<string>(s => s.Contains("Failed to verify selected config PIN", StringComparison.Ordinal)),
                fatal),
            Times.Once);
        _startupUi.Verify(u => u.ConfirmStartFresh(false), Times.Once);
        _appInit.Verify(a => a.InitializeNewDatabase(It.IsAny<AppDatabase>()), Times.Once);
    }

    [Fact]
    public void Run_RememberPinResealFailure_LogsDisablesAndContinuesToMainForm()
    {
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone(), pinBypassed: false);
        SetupCredentialResult(credResult);
        SetupFirstRunScope();

        _rememberPinService.Setup(r => r.IsEnabled).Returns(true);
        _rememberPinService.Setup(r => r.UpdateForPinChange(It.IsAny<ISecureSecretSnapshotSource>()))
            .Throws(new InvalidOperationException("Simulated reseal failure"));

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("Failed to refresh Remember PIN key"))), Times.Once);
        _rememberPinService.Verify(r => r.Disable(), Times.Once);
        _mainFormRunner.Verify(r => r.Run(It.IsAny<ILifetimeScope>()), Times.Once);
    }

    [Fact]
    public void Run_RememberPinResealFailure_DisableAlsoFails_LogsBothAndContinues()
    {
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone(), pinBypassed: false);
        SetupCredentialResult(credResult);
        SetupFirstRunScope();

        _rememberPinService.Setup(r => r.IsEnabled).Returns(true);
        _rememberPinService.Setup(r => r.UpdateForPinChange(It.IsAny<ISecureSecretSnapshotSource>()))
            .Throws(new InvalidOperationException("reseal failure"));
        _rememberPinService.Setup(r => r.Disable())
            .Throws(new InvalidOperationException("cleanup failure"));

        var code = BuildOrchestrator().Run(isBackground: false);

        Assert.Equal(0, code);
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("Failed to refresh Remember PIN key"))), Times.Once);
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("Failed to clean up Remember PIN key material"))), Times.Once);
        _mainFormRunner.Verify(r => r.Run(It.IsAny<ILifetimeScope>()), Times.Once);
    }

    [Fact]
    public void Run_PinBypassed_SkipsRememberPinResealEvenWhenEnabled()
    {
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone(), pinBypassed: true);
        SetupCredentialResult(credResult);
        SetupFirstRunScope();

        _rememberPinService.Setup(r => r.IsEnabled).Returns(true);

        BuildOrchestrator().Run(isBackground: false);

        _rememberPinService.Verify(r => r.UpdateForPinChange(It.IsAny<ISecureSecretSnapshotSource>()), Times.Never);
    }

    [Fact]
    public void Run_Background_PassesIsBackgroundTrueToSessionScope()
    {
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone(), pinBypassed: false);
        SetupCredentialResult(credResult);

        StartupOptions? capturedOptions = null;
        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(ConfigIntegrityResult.FirstRun);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, It.IsAny<AppDatabase>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Callback<SessionContext, StartupOptions>((_, opts) => capturedOptions = opts)
            .Returns(mockScope.Object);

        BuildOrchestrator().Run(isBackground: true);

        Assert.NotNull(capturedOptions);
        Assert.True(capturedOptions!.IsBackground);
        Assert.False(capturedOptions.GrantStartupRunAsUnlock);
    }

    [Fact]
    public void Run_BackgroundRunAsStartup_PassesStartupRunAsUnlockGrantToSessionScope()
    {
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone(), pinBypassed: false);
        SetupCredentialResult(credResult);

        StartupOptions? capturedOptions = null;
        _databaseService.Setup(d => d.VerifyConfigIntegrity(It.IsAny<ISecureSecretSnapshotSource>()))
            .Returns(ConfigIntegrityResult.FirstRun);
        _appInit.Setup(a => a.EnsureCurrentAccountCredential(_store, It.IsAny<AppDatabase>())).Returns(false);

        var mockScope = BuildMockScope();
        _scopeFactory.Setup(f => f.BeginSessionScope(It.IsAny<SessionContext>(), It.IsAny<StartupOptions>()))
            .Callback<SessionContext, StartupOptions>((_, opts) => capturedOptions = opts)
            .Returns(mockScope.Object);

        BuildOrchestrator().Run(isBackground: true, grantStartupRunAsUnlock: true);

        Assert.NotNull(capturedOptions);
        Assert.True(capturedOptions!.IsBackground);
        Assert.True(capturedOptions.GrantStartupRunAsUnlock);
    }

    [Fact]
    public void Run_DuplicateSidInCredentialStore_LogsWarning()
    {
        _store.Credentials.Add(new CredentialEntry { Sid = "S-1-5-21-100-200-300-1001", EncryptedPassword = [] });
        _store.Credentials.Add(new CredentialEntry { Sid = "S-1-5-21-100-200-300-1001", EncryptedPassword = [] });

        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        SetupCredentialResult(credResult);
        SetupFirstRunScope();

        BuildOrchestrator().Run(isBackground: false);

        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("Duplicate credential SID detected"))), Times.Once);
    }

    [Fact]
    public void Run_SessionScopeDisposedAfterMainFormRunnerReturns()
    {
        using var credResult = CreateCredentialResult((byte[])ValidKey32.Clone());
        SetupCredentialResult(credResult);
        var mockScope = SetupFirstRunScope();

        BuildOrchestrator().Run(isBackground: false);

        mockScope.Verify(s => s.Dispose(), Times.Once);
    }
}
