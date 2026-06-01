using RunFence.Core.Helpers;

namespace RunFence.Launch;

public sealed record AssociationLaunchRequest(
    string AssociationKey,
    string? RawArgument,
    string? NormalizedTarget)
{
    public static AssociationLaunchRequest Build(string associationKey, string? rawArgument)
    {
        var normalizedTarget = AssociationCommandHelper.ParseAssociationTarget(rawArgument);
        return new AssociationLaunchRequest(
            associationKey,
            rawArgument,
            normalizedTarget);
    }
}
