namespace RunFence.Account;

public interface IGroupPolicyScriptHelper
{
    bool IsLoginBlocked(string sid);
    SetLoginBlockedResult SetLoginBlocked(string sid, bool blocked);
}
