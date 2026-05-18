namespace RunFence.Firewall;

public interface IEventLogRecordSource
{
    IEnumerable<EventLogRecordSnapshot> Read(string logName, string xPathQuery);
}
