using RunFence.Core.Models;

namespace RunFence.Account;

public interface IWindowsAccountQueryService
{
    IReadOnlyList<LocalUserAccount> GetLocalUsers();
    AccountQueryResult TryGetUser(string sid);
    AccountQueryResult GetProfilePath(string sid);
    AccountQueryResult IsInteractiveUser(string sid);
    AccountQueryResult GetInteractiveUserSid();
}
