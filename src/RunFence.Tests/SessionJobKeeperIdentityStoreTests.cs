using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Startup;
using Xunit;

namespace RunFence.Tests;

public class SessionJobKeeperIdentityStoreTests : IDisposable
{
    private const string Sid = "S-1-5-21-100-200-300-1001";

    private readonly ProtectedBuffer _pinKey = new(new byte[32], protect: false);
    private readonly Mock<IConfigRepository> _configRepository = new();
    private readonly AppDatabase _database = new();
    private readonly SessionJobKeeperIdentityStore _store;

    public SessionJobKeeperIdentityStoreTests()
    {
        var sessionProvider = new SessionProvider();
        sessionProvider.SetSession(new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore { ArgonSalt = new byte[32] },
            PinDerivedKey = _pinKey,
        });
        _store = new SessionJobKeeperIdentityStore(sessionProvider, _configRepository.Object);
    }

    public void Dispose() => _pinKey.Dispose();

    [Fact]
    public void CreateFresh_PersistsIdentityForSidAndMode()
    {
        var identity = _store.CreateFresh(Sid, isLow: false);

        Assert.NotNull(_database.JobKeeperInstances);
        Assert.Same(identity, _database.JobKeeperInstances[JobKeeperInstanceIdentity.CreateKey(Sid, false)]);
        Assert.Equal(Sid, identity.TargetSid);
        Assert.Equal(JobKeeperIntegrityMode.Restricted, identity.ExpectedMode);
        Assert.Contains(identity.InstanceId, identity.PipeName);
        Assert.Contains(identity.InstanceId, identity.JobName);
        Assert.DoesNotContain(Sid, identity.PipeName);
        Assert.DoesNotContain(Sid, identity.JobName);
        Assert.True(identity.PipeName.Length < 100);
        Assert.True(identity.JobName.Length < 100);
        _configRepository.Verify(r => r.SaveConfig(_database, It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Once);
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
                JobName = "job",
            },
            [JobKeeperInstanceIdentity.CreateKey(Sid, true)] = new()
            {
                TargetSid = Sid,
                ExpectedMode = JobKeeperIntegrityMode.LowIntegrity,
                InstanceId = "other",
                PipeName = "pipe",
                JobName = "job",
            },
        };

        var result = _store.Get(Sid, isLow: false);

        Assert.Null(result);
        Assert.False(_database.JobKeeperInstances.ContainsKey(JobKeeperInstanceIdentity.CreateKey(Sid, false)));
        Assert.True(_database.JobKeeperInstances.ContainsKey(JobKeeperInstanceIdentity.CreateKey(Sid, true)));
        _configRepository.Verify(r => r.SaveConfig(_database, It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public void UpdateLastVerifiedPid_PersistsPid()
    {
        var identity = _store.CreateFresh(Sid, isLow: true);
        _configRepository.Invocations.Clear();

        _store.UpdateLastVerifiedPid(identity, 1234);

        Assert.Equal(1234, identity.LastVerifiedKeeperPid);
        _configRepository.Verify(r => r.SaveConfig(_database, It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Once);
    }
}
