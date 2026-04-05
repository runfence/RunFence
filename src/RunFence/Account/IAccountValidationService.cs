namespace RunFence.Account;

public interface IAccountValidationService
{
    void ValidateNotCurrentAccount(string sid, string action);
    void ValidateNotLastAdmin(string sid, string action);
    void ValidateNotInteractiveUser(string sid, string action);
    void ValidateNoRunningProcesses(string sid, string action);
    List<string> GetProcessesRunningAsSid(string targetSid);
}