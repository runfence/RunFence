namespace RunFence.Account;

public interface IProcessListService
{
    IReadOnlyList<ProcessInfo> GetProcessesForSid(string sid, CancellationToken cancellationToken = default);
    HashSet<string> GetSidsWithProcesses(IEnumerable<string> sids, CancellationToken cancellationToken = default);
}
