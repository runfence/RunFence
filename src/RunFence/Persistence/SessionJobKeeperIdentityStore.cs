using System.Security.Cryptography;
using System.Text;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Persistence;

public sealed class SessionJobKeeperIdentityStore(
    ISessionProvider sessionProvider,
    Func<IUiThreadInvoker> uiThreadInvokerFactory,
    Func<IMainConfigPersistence> mainConfigPersistenceFactory) : IJobKeeperIdentityStore
{
    public JobKeeperInstanceIdentity? Get(string sid, bool isLow)
        => uiThreadInvokerFactory().Invoke(() =>
        {
            var database = sessionProvider.GetSession().Database;
            if (database.JobKeeperInstances == null)
                return null;

            var key = JobKeeperInstanceIdentity.CreateKey(sid, isLow);
            if (!database.JobKeeperInstances.TryGetValue(key, out var identity))
                return null;

            var expectedMode = JobKeeperInstanceIdentity.GetMode(isLow);
            if (string.Equals(identity.TargetSid, sid, StringComparison.OrdinalIgnoreCase)
                && identity.ExpectedMode == expectedMode
                && !string.IsNullOrWhiteSpace(identity.PipeName))
            {
                return identity;
            }

            RemoveCore(sid, isLow);
            return null;
        });

    public IReadOnlyList<JobKeeperInstanceIdentity> GetAll()
        => uiThreadInvokerFactory().Invoke(() =>
            sessionProvider.GetSession().Database.JobKeeperInstances?
                .Values
                .Where(IsValid)
                .Select(identity => identity with { })
                .ToList()
            ?? []);

    public JobKeeperInstanceIdentity CreateFresh(string sid, bool isLow)
        => uiThreadInvokerFactory().Invoke(() =>
        {
            var mode = JobKeeperInstanceIdentity.GetMode(isLow);
            var instanceId = Guid.NewGuid().ToString("N");
            var nameKey = CreateNameKey(sid, mode);
            var identity = new JobKeeperInstanceIdentity
            {
                TargetSid = sid,
                ExpectedMode = mode,
                InstanceId = instanceId,
                PipeName = $"RunFence-JK-{nameKey}-{instanceId}",
            };

            var database = sessionProvider.GetSession().Database;
            database.JobKeeperInstances ??= new Dictionary<string, JobKeeperInstanceIdentity>(StringComparer.OrdinalIgnoreCase);
            database.JobKeeperInstances[JobKeeperInstanceIdentity.CreateKey(sid, isLow)] = identity;
            SaveConfig();
            return identity;
        });

    public void Remove(string sid, bool isLow)
        => uiThreadInvokerFactory().Invoke(() => RemoveCore(sid, isLow));

    public void UpdateLastVerifiedPid(JobKeeperInstanceIdentity identity, int keeperPid)
        => uiThreadInvokerFactory().Invoke(() =>
        {
            identity.LastVerifiedKeeperPid = keeperPid;
            SaveConfig();
        });

    private void RemoveCore(string sid, bool isLow)
    {
        var database = sessionProvider.GetSession().Database;
        if (database.JobKeeperInstances?.Remove(JobKeeperInstanceIdentity.CreateKey(sid, isLow)) != true)
            return;

        if (database.JobKeeperInstances.Count == 0)
            database.JobKeeperInstances = null;
        SaveConfig();
    }

    private void SaveConfig()
    {
        var session = sessionProvider.GetSession();
        var pinKeySource = session.PinDerivedKey;
        mainConfigPersistenceFactory().SaveConfig(session.Database, pinKeySource, session.CredentialStore.ArgonSalt);
    }

    private static string CreateNameKey(string sid, JobKeeperIntegrityMode mode)
    {
        var input = Encoding.UTF8.GetBytes($"{sid}|{mode}");
        var hash = SHA256.HashData(input);
        return $"{(mode == JobKeeperIntegrityMode.LowIntegrity ? "L" : "R")}-{Convert.ToHexString(hash, 0, 12)}";
    }

    private static bool IsValid(JobKeeperInstanceIdentity identity) =>
        !string.IsNullOrWhiteSpace(identity.TargetSid)
        && !string.IsNullOrWhiteSpace(identity.PipeName)
        && Enum.IsDefined(typeof(JobKeeperIntegrityMode), identity.ExpectedMode);
}
