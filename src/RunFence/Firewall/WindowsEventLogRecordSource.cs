using System.Diagnostics.Eventing.Reader;

namespace RunFence.Firewall;

public class WindowsEventLogRecordSource : IEventLogRecordSource
{
    public IEnumerable<EventLogRecordSnapshot> Read(string logName, string xPathQuery)
    {
        var query = new EventLogQuery(logName, PathType.LogName, xPathQuery);

        using var reader = new EventLogReader(query);
        while (reader.ReadEvent() is { } record)
        {
            using (record)
            {
                yield return new EventLogRecordSnapshot(
                    record.Properties.Select(p => p.Value).ToList(),
                    record.TimeCreated);
            }
        }
    }
}
