using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Persistence;

public sealed class SessionTrackingJobStateStore(
    ISessionProvider sessionProvider,
    Func<IUiThreadInvoker> uiThreadInvokerFactory,
    ISessionSaver sessionSaver) : ITrackingJobStateStore
{
    public bool ContainsTrackingJobSid(string sid)
        => uiThreadInvokerFactory().Invoke(() =>
        {
            if (string.IsNullOrWhiteSpace(sid))
                return false;

            var trackingJobSids = sessionProvider.GetSession().Database.TrackingJobSids;
            return trackingJobSids != null
                && trackingJobSids.Any(existing => string.Equals(existing, sid, StringComparison.OrdinalIgnoreCase));
        });

    public void AddTrackingJobSid(string sid)
        => uiThreadInvokerFactory().Invoke(() =>
        {
            if (string.IsNullOrWhiteSpace(sid))
                return;

            var database = sessionProvider.GetSession().Database;
            database.TrackingJobSids ??= [];

            if (database.TrackingJobSids.Any(existing =>
                    string.Equals(existing, sid, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            database.TrackingJobSids.Add(sid);
            SaveConfigIfNeeded(saveImmediately: true);
        });

    public void RemoveTrackingJobSid(string sid, bool saveImmediately = true)
        => uiThreadInvokerFactory().Invoke(() =>
        {
            if (string.IsNullOrWhiteSpace(sid))
                return;

            var database = sessionProvider.GetSession().Database;
            if (database.TrackingJobSids == null)
                return;

            var removed = database.TrackingJobSids.RemoveAll(existing =>
                string.Equals(existing, sid, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                return;

            if (database.TrackingJobSids.Count == 0)
                database.TrackingJobSids = null;

            SaveConfigIfNeeded(saveImmediately);
        });

    public void MigrateTrackingJobSid(string oldSid, string newSid, bool saveImmediately = true)
        => uiThreadInvokerFactory().Invoke(() =>
        {
            if (string.IsNullOrWhiteSpace(oldSid))
                return;

            var database = sessionProvider.GetSession().Database;
            if (database.TrackingJobSids == null)
                return;

            var removed = database.TrackingJobSids.RemoveAll(existing =>
                string.Equals(existing, oldSid, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                return;

            if (!string.IsNullOrWhiteSpace(newSid)
                && !database.TrackingJobSids.Any(existing =>
                    string.Equals(existing, newSid, StringComparison.OrdinalIgnoreCase)))
            {
                database.TrackingJobSids.Add(newSid);
            }

            if (database.TrackingJobSids.Count == 0)
                database.TrackingJobSids = null;

            SaveConfigIfNeeded(saveImmediately);
        });

    private void SaveConfigIfNeeded(bool saveImmediately)
    {
        if (!saveImmediately)
            return;

        sessionSaver.SaveConfig();
    }
}
