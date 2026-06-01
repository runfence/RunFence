using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.UI;

public sealed class FullModeAccountLaunchIdentityFactory(ILocalGroupQueryService localGroupQueryService)
{
    private const string AdministratorsSid = "S-1-5-32-544";

    public AccountLaunchIdentity Create(string accountSid, bool fullMode)
        => new(accountSid)
        {
            PrivilegeLevel = fullMode ? ResolveFullModePrivilege(accountSid) : null
        };

    private PrivilegeLevel ResolveFullModePrivilege(string accountSid)
        => localGroupQueryService.GetGroupsForUser(accountSid)
            .Any(group => string.Equals(group.Sid, AdministratorsSid, StringComparison.OrdinalIgnoreCase))
            ? PrivilegeLevel.HighestAllowed
            : PrivilegeLevel.Basic;
}
