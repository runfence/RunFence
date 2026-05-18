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
        SavedRightsState? savedRights = null,
        string? ownerSid = null)
        => service.AddGrant(sid, path, isDeny, savedRights, confirm: isDeny ? static () => true : null);

    public static GrantApplyResult AddGrant(
        this PathGrantService service,
        string sid,
        string path,
        bool isDeny,
        SavedRightsState? savedRights = null,
        string? ownerSid = null)
        => service.AddGrant(sid, path, isDeny, savedRights, confirm: isDeny ? static () => true : null);

    public static GrantApplyResult UpdateGrant(
        this IGrantMutatorService service,
        string sid,
        string path,
        bool isDeny,
        SavedRightsState savedRights,
        string? ownerSid = null)
        => service.UpdateGrant(sid, path, isDeny, savedRights, confirm: isDeny ? static () => true : null);

    public static GrantApplyResult UpdateGrant(
        this PathGrantService service,
        string sid,
        string path,
        bool isDeny,
        SavedRightsState savedRights,
        string? ownerSid = null)
        => service.UpdateGrant(sid, path, isDeny, savedRights, confirm: isDeny ? static () => true : null);
}
