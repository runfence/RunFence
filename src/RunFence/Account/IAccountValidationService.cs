namespace RunFence.Account;

public interface IAccountValidationService
{
    void ValidateNotCurrentAccount(string sid, string action);
    void ValidateNotLastAdmin(string sid, string action);
    void ValidateNotInteractiveUser(string sid, string action);
    IReadOnlyList<ProcessInfo> GetRunningProcesses(string targetSid);
    List<string> GetProcessesRunningAsSid(string targetSid);
}
