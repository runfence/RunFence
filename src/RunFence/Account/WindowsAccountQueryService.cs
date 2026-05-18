using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Acl.Permissions;

namespace RunFence.Account;

public class WindowsAccountQueryService(
    ILocalUserProvider localUserProvider,
    IProfilePathResolver profilePathResolver,
    IInteractiveUserResolver interactiveUserResolver,
    ILoggingService log) : IWindowsAccountQueryService
{
    public IReadOnlyList<LocalUserAccount> GetLocalUsers() => localUserProvider.GetLocalUserAccounts();

    public AccountQueryResult TryGetUser(string sid)
    {
        try
        {
            var account = localUserProvider.GetLocalUserAccounts()
                .FirstOrDefault(u => string.Equals(u.Sid, sid, StringComparison.OrdinalIgnoreCase));
            return account == null
                ? new(AccountQueryStatus.NotFound, null, null, null, null, null)
                : new(AccountQueryStatus.Succeeded, account, null, null, null, null);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to query user by SID {sid}", ex);
            return new(AccountQueryStatus.Failed, null, null, null, null, ex.Message);
        }
    }

    public AccountQueryResult GetProfilePath(string sid)
    {
        try
        {
            var profilePath = profilePathResolver.TryGetProfilePath(sid);
            return profilePath == null
                ? new(AccountQueryStatus.NotFound, null, null, null, null, null)
                : new(AccountQueryStatus.Succeeded, null, profilePath, null, null, null);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to query profile path for SID {sid}", ex);
            return new(AccountQueryStatus.Failed, null, null, null, null, ex.Message);
        }
    }

    public AccountQueryResult IsInteractiveUser(string sid)
    {
        try
        {
            var interactiveSid = interactiveUserResolver.GetInteractiveUserSid();
            var isInteractive = interactiveSid != null &&
                                string.Equals(interactiveSid, sid, StringComparison.OrdinalIgnoreCase);
            return new(AccountQueryStatus.Succeeded, null, null, interactiveSid, isInteractive, null);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new(AccountQueryStatus.AccessDenied, null, null, null, null, ex.Message);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to query interactive user state for SID {sid}", ex);
            return new(AccountQueryStatus.Failed, null, null, null, null, ex.Message);
        }
    }

    public AccountQueryResult GetInteractiveUserSid()
    {
        try
        {
            var sid = interactiveUserResolver.GetInteractiveUserSid();
            return sid == null
                ? new(AccountQueryStatus.NotFound, null, null, null, null, null)
                : new(AccountQueryStatus.Succeeded, null, null, sid, null, null);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new(AccountQueryStatus.AccessDenied, null, null, null, null, ex.Message);
        }
        catch (Exception ex)
        {
            log.Error("Failed to query interactive user SID", ex);
            return new(AccountQueryStatus.Failed, null, null, null, null, ex.Message);
        }
    }
}
