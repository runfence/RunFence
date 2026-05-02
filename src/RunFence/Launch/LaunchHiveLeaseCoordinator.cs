using RunFence.Core.Helpers;
using RunFence.Infrastructure;

namespace RunFence.Launch;

public class LaunchHiveLeaseCoordinator(IUserHiveManager hiveManager)
{
    public IDisposable? EnsureHiveLoaded(string sid)
        => hiveManager.EnsureHiveLoaded(sid);

    public bool ShouldUseInteractiveFallback(string launchedSid, string? interactiveSid)
        => !string.IsNullOrWhiteSpace(interactiveSid)
           && !SidComparer.SidEquals(interactiveSid, launchedSid);

    public IDisposable? TakeCombinedLease(ref IDisposable? launchedLease, ref IDisposable? interactiveLease)
    {
        var firstLease = launchedLease;
        var secondLease = interactiveLease;
        launchedLease = null;
        interactiveLease = null;

        if (firstLease == null)
            return secondLease;
        if (secondLease == null)
            return firstLease;

        return new CombinedLease(firstLease, secondLease);
    }

    private sealed class CombinedLease(params IDisposable[] leases) : IDisposable
    {
        public void Dispose()
        {
            List<Exception>? errors = null;

            foreach (var lease in leases)
            {
                try
                {
                    lease.Dispose();
                }
                catch (Exception ex)
                {
                    errors ??= [];
                    errors.Add(ex);
                }
            }

            if (errors is { Count: > 0 })
                throw new AggregateException(errors);
        }
    }
}
