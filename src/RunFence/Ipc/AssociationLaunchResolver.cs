using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Models;
using RunFence.Launch;
using RunFence.Persistence;

namespace RunFence.Ipc;

public class AssociationLaunchResolver(
    Func<IHandlerMappingService> handlerMappingService,
    IIpcCallerAuthorizer authorizer)
    : IAssociationLaunchResolver
{
    private IHandlerMappingService HandlerMappingService => handlerMappingService();

    public AssociationLaunchResolution Resolve(
        AppDatabase database,
        string association,
        string? arguments,
        string? callerIdentity,
        string? callerSid,
        bool identityFromImpersonation)
        => Resolve(
            database,
            BuildRequest(association, arguments),
            callerIdentity,
            callerSid,
            identityFromImpersonation);

    public AssociationLaunchResolution Resolve(
        AppDatabase database,
        AssociationLaunchRequest request,
        string? callerIdentity,
        string? callerSid,
        bool identityFromImpersonation)
    {
        var allMappings = HandlerMappingService.GetAllHandlerMappings(database);

        if (!allMappings.TryGetValue(request.AssociationKey, out var entries))
            return new AssociationLaunchResolution(AssociationLaunchResolutionStatus.UnknownAssociation);

        AppEntry? authorizedApp = null;
        HandlerMappingEntry? matchedEntry = null;
        var anyFound = false;
        var anyAuthorized = false;

        foreach (var entry in entries)
        {
            var candidate = database.Apps.FirstOrDefault(a => a.Id == entry.AppId);
            if (candidate == null)
                continue;

            anyFound = true;

            if (!authorizer.IsCallerAuthorizedForAssociation(
                    callerIdentity,
                    callerSid,
                    candidate,
                    database,
                    identityFromImpersonation))
            {
                continue;
            }

            anyAuthorized = true;

            if (!MatchesEffectivePrefixes(entry, candidate, request.NormalizedTarget))
                continue;

            if (authorizer.HasExplicitPerAppAuthorization(callerSid, candidate, database))
                return new AssociationLaunchResolution(AssociationLaunchResolutionStatus.Success, candidate, entry);

            authorizedApp ??= candidate;
            matchedEntry ??= entry;
        }

        if (authorizedApp != null && matchedEntry != null)
            return new AssociationLaunchResolution(AssociationLaunchResolutionStatus.Success, authorizedApp, matchedEntry);

        if (!anyFound)
            return new AssociationLaunchResolution(AssociationLaunchResolutionStatus.AppNotFound);

        return anyAuthorized
            ? new AssociationLaunchResolution(AssociationLaunchResolutionStatus.PathPrefixMismatch)
            : new AssociationLaunchResolution(AssociationLaunchResolutionStatus.AccessDenied);
    }

    public static AssociationLaunchRequest BuildRequest(string associationKey, string? rawArgument)
    {
        var normalizedTarget = AssociationCommandHelper.ParseAssociationTarget(rawArgument);
        return new AssociationLaunchRequest(
            associationKey,
            rawArgument,
            normalizedTarget);
    }

    private static bool MatchesEffectivePrefixes(HandlerMappingEntry entry, AppEntry app, string? normalizedTarget)
    {
        List<string>? effective;
        if (entry.ReplacePrefixes)
            effective = entry.PathPrefixes;
        else
        {
            var appP = app.PathPrefixes;
            var entP = entry.PathPrefixes;
            effective = (appP?.Count > 0 || entP?.Count > 0)
                ? (appP ?? []).Concat(entP ?? []).ToList()
                : null;
        }

        if (effective is not { Count: > 0 })
            return true;
        if (normalizedTarget == null)
            return false;
        return effective.Any(p => MatchesPrefix(normalizedTarget, p));
    }

    private static bool MatchesPrefix(string value, string prefix)
    {
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        if (value.Length == prefix.Length)
            return true;
        if (prefix[^1] is '/' or '\\')
            return true;
        return value[prefix.Length] is '/' or '\\' or '?' or '#';
    }
}
