using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Startup;
using Xunit;

namespace RunFence.Tests;

public class SessionJobKeeperIdentityStoreTests : IDisposable
{
    private const string Sid = "S-1-5-21-100-200-300-1001";
    private const string OtherSid = "S-1-5-21-100-200-300-1002";
    private const string ExtraConfigPath = @"D:\Configs\extra.rfn";

    private readonly SecureSecret _pinKey = TestSecretFactory.Create(32);
    private readonly Mock<IMainConfigPersistence> _configRepository = new();
    private readonly AppDatabase _database = new();
    private readonly IUiThreadInvoker _uiThreadInvoker;
    private readonly SessionJobKeeperIdentityStore _store;
    private readonly TempDirectory _tempDir;

    public SessionJobKeeperIdentityStoreTests()
        : this(new InlineUiThreadInvoker(action => action()))
    {
    }

    private SessionJobKeeperIdentityStoreTests(IUiThreadInvoker uiThreadInvoker)
    {
        _tempDir = new TempDirectory("RunFence_JobKeeperIdentityStore");
        _uiThreadInvoker = uiThreadInvoker;
        var sessionProvider = new SessionProvider();
        sessionProvider.SetSession(new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore { ArgonSalt = new byte[32] },
        }.WithClonedPinDerivedKey(_pinKey));
        _store = new SessionJobKeeperIdentityStore(sessionProvider, () => _uiThreadInvoker, () => _configRepository.Object);
    }

    public void Dispose()
    {
        _pinKey.Dispose();
        _tempDir.Dispose();
    }

    [Fact]
    public void CreateFresh_PersistsIdentityForSidAndMode()
    {
        var identity = _store.CreateFresh(Sid, isLow: false);

        Assert.NotNull(_database.JobKeeperInstances);
        Assert.Same(identity, _database.JobKeeperInstances[JobKeeperInstanceIdentity.CreateKey(Sid, false)]);
        Assert.Equal(Sid, identity.TargetSid);
        Assert.Equal(JobKeeperIntegrityMode.Restricted, identity.ExpectedMode);
        Assert.Contains(identity.InstanceId, identity.PipeName);
        Assert.DoesNotContain(Sid, identity.PipeName);
        Assert.True(identity.PipeName.Length < 100);
        _configRepository.Verify(
            r => r.SaveConfig(_database, It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()),
            Times.Once);
    }

    [Fact]
    public void Get_MalformedPersistedIdentity_RemovesOnlyThatSidMode()
    {
        _database.JobKeeperInstances = new Dictionary<string, JobKeeperInstanceIdentity>(StringComparer.OrdinalIgnoreCase)
        {
            [JobKeeperInstanceIdentity.CreateKey(Sid, false)] = new()
            {
                TargetSid = Sid,
                ExpectedMode = JobKeeperIntegrityMode.Restricted,
                InstanceId = "broken",
                PipeName = "",
            },
            [JobKeeperInstanceIdentity.CreateKey(Sid, true)] = new()
            {
                TargetSid = Sid,
                ExpectedMode = JobKeeperIntegrityMode.LowIntegrity,
                InstanceId = "other",
                PipeName = "pipe",
            },
        };

        var result = _store.Get(Sid, isLow: false);

        Assert.Null(result);
        Assert.False(_database.JobKeeperInstances.ContainsKey(JobKeeperInstanceIdentity.CreateKey(Sid, false)));
        Assert.True(_database.JobKeeperInstances.ContainsKey(JobKeeperInstanceIdentity.CreateKey(Sid, true)));
        _configRepository.Verify(
            r => r.SaveConfig(_database, It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()),
            Times.Once);
    }

    [Fact]
    public void UpdateLastVerifiedPid_PersistsPid()
    {
        var identity = _store.CreateFresh(Sid, isLow: true);
        _configRepository.Invocations.Clear();

        _store.UpdateLastVerifiedPid(identity, 1234);

        Assert.Equal(1234, identity.LastVerifiedKeeperPid);
        _configRepository.Verify(
            r => r.SaveConfig(_database, It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()),
            Times.Once);
    }

    [Fact]
    public void Get_SeparateOldAndNewSidEntries_DoNotOverwriteEachOther()
    {
        var oldIdentity = _store.CreateFresh(Sid, isLow: false);
        var newIdentity = _store.CreateFresh(OtherSid, isLow: false);

        Assert.Same(oldIdentity, _store.Get(Sid, isLow: false));
        Assert.Same(newIdentity, _store.Get(OtherSid, isLow: false));
        Assert.NotSame(oldIdentity, newIdentity);
    }

    [Fact]
    public void GetAll_ReturnsValidPersistedIdentities()
    {
        var restrictedIdentity = _store.CreateFresh(Sid, isLow: false);
        var lowIdentity = _store.CreateFresh(OtherSid, isLow: true);
        _database.JobKeeperInstances!["broken"] = new JobKeeperInstanceIdentity
        {
            TargetSid = "",
            ExpectedMode = JobKeeperIntegrityMode.Restricted,
            InstanceId = "broken",
            PipeName = "pipe",
        };

        var result = _store.GetAll();

        Assert.Equal(
            [restrictedIdentity.InstanceId, lowIdentity.InstanceId],
            result.Select(identity => identity.InstanceId));
        Assert.NotSame(restrictedIdentity, result[0]);
        Assert.NotSame(lowIdentity, result[1]);
    }

    [Fact]
    public async Task CreateFresh_WorkerThread_MarshalsMutationAndSaveToUiThread()
    {
        using var uiInvoker = new DedicatedThreadUiInvoker();
        using var fixture = new SessionJobKeeperIdentityStoreTests(uiInvoker);

        var mutationThreadId = 0;
        fixture._configRepository
            .Setup(r => r.SaveConfig(fixture._database, It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
            .Callback(() => mutationThreadId = Environment.CurrentManagedThreadId);

        var identity = await Task.Run(() => fixture._store.CreateFresh(Sid, isLow: false));

        Assert.Same(
            identity,
            fixture._database.JobKeeperInstances![JobKeeperInstanceIdentity.CreateKey(Sid, false)]);
        Assert.Equal(uiInvoker.ThreadId, mutationThreadId);
    }

    [Fact]
    public async Task UpdateLastVerifiedPid_WorkerThread_MarshalsSaveToUiThread()
    {
        using var uiInvoker = new DedicatedThreadUiInvoker();
        using var fixture = new SessionJobKeeperIdentityStoreTests(uiInvoker);
        var identity = fixture._store.CreateFresh(Sid, isLow: false);
        fixture._configRepository.Invocations.Clear();

        var saveThreadId = 0;
        fixture._configRepository
            .Setup(r => r.SaveConfig(fixture._database, It.IsAny<ISecureSecretSnapshotSource>(), It.IsAny<byte[]>()))
            .Callback(() => saveThreadId = Environment.CurrentManagedThreadId);

        await Task.Run(() => fixture._store.UpdateLastVerifiedPid(identity, 5678));

        Assert.Equal(5678, identity.LastVerifiedKeeperPid);
        Assert.Equal(uiInvoker.ThreadId, saveThreadId);
    }

    [Fact]
    public void CreateFresh_SaveConfig_UsesSessionScopedFilterAndKeepsExtraConfigAppsOutOfMainConfig()
    {
        var ownershipProjection = new GrantIntentOwnershipProjectionService();
        var appIdValidator = new AppIdValidator();
        var appFilter = new AppConfigIndex(ownershipProjection, appIdValidator);
        appFilter.AssignApp("extra-app", ExtraConfigPath);

        var database = new AppDatabase();
        database.Apps.Add(new AppEntry
        {
            Id = "main-app",
            Name = "Main App",
            ExePath = @"C:\main.exe"
        });
        database.Apps.Add(new AppEntry
        {
            Id = "extra-app",
            Name = "Extra App",
            ExePath = @"D:\extra.exe"
        });

        var sessionProvider = new SessionProvider();
        using var pinKey = TestSecretFactory.Create(32);
        var session = new SessionContext
        {
            Database = database,
            CredentialStore = new CredentialStore { ArgonSalt = new byte[32] }
        }.WithClonedPinDerivedKey(pinKey);
        sessionProvider.SetSession(session);

        var databaseService = new DatabaseService(
            Mock.Of<ILoggingService>(),
            new TestConfigPaths(_tempDir.Path),
            new PersistenceAtomicFileWriter(new PersistenceFileSecurityMirror()),
            appFilter,
            allowPlaintextConfig: false);

        var store = new SessionJobKeeperIdentityStore(
            sessionProvider,
            () => _uiThreadInvoker,
            () => databaseService);

        store.CreateFresh(Sid, isLow: false);

        var persistedMainConfig = databaseService.LoadConfig(pinKey);
        Assert.Collection(
            persistedMainConfig.Apps,
            app => Assert.Equal("main-app", app.Id));
    }

    private sealed class TestConfigPaths(string dir) : IConfigPaths
    {
        public string ConfigFilePath => Path.Combine(dir, "config.dat");
        public string CredentialsFilePath => Path.Combine(dir, "credentials.dat");
        public string LicenseFilePath => Path.Combine(dir, "license.dat");
        public string RememberPinFilePath => Path.Combine(dir, "startkey.dat");
        public string LocalDataDir => dir;
    }
}
