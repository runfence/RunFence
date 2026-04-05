namespace RunFence.Account;

public interface IProcessListService
{
    IReadOnlyList<ProcessInfo> GetProcessesForSid(string sid);
    HashSet<string> GetSidsWithProcesses(IEnumerable<string> sids);
}