using RunFence.Acl;
using RunFence.Core.Models;

namespace RunFence.Tests;

internal static class GrantMutatorTestExtensions
{
    public static GrantApplyResult AddGrant(
        this IGrantMutatorService service,
        string sid,
        string path,
        bool isDeny,
        SavedRightsState? savedRights = null)
        => service.AddGrant(sid, path, isDeny, savedRights, confirm: isDeny ? static () => true : null);

    public static GrantApplyResult UpdateGrant(
        this IGrantMutatorService service,
        string sid,
        string path,
        bool isDeny,
        SavedRightsState savedRights)
        => service.UpdateGrant(sid, path, isDeny, savedRights, confirm: isDeny ? static () => true : null);

}
