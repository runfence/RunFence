namespace RunFence.Persistence;

public interface ITrackingJobStateStore
{
    void AddTrackingJobSid(string sid);
    bool ContainsTrackingJobSid(string sid);
    void RemoveTrackingJobSid(string sid, bool saveImmediately = true);
    void MigrateTrackingJobSid(string oldSid, string newSid, bool saveImmediately = true);
}
