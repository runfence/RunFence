using RunFence.Core.Models;

namespace RunFence.Launch;

public interface IAssociationLaunchResolver
{
    AssociationLaunchResolution Resolve(
        AppDatabase database,
        string association,
        string? arguments,
        string? callerIdentity,
        string? callerSid,
        bool identityFromImpersonation);

    AssociationLaunchResolution Resolve(
        AppDatabase database,
        AssociationLaunchRequest request,
        string? callerIdentity,
        string? callerSid,
        bool identityFromImpersonation);
}
