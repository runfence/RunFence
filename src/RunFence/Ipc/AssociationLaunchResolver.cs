using RunFence.Core.Models;
using RunFence.Launch;
using RunFence.Persistence;

namespace RunFence.Ipc;

public class AssociationLaunchResolver(
    IHandlerMappingService handlerMappingService,
    IIpcCallerAuthorizer authorizer)
    : IAssociationLaunchResolver
{
    public AssociationLaunchResolution Resolve(
        AppDatabase database,
        string association,
        string? arguments,
        string? callerIdentity,
        string? callerSid,
        bool identityFromImpersonation)
    {
        var allMappings = handlerMappingService.GetAllHandlerMappings(database);

        if (!allMappings.TryGetValue(association, out var entries))
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

            if (!MatchesEffectivePrefixes(entry, candidate, arguments))
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

    private static bool MatchesEffectivePrefixes(HandlerMappingEntry entry, AppEntry app, string? arguments)
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
        if (arguments == null)
            return false;

        string normalized;
        if (arguments.Contains("://"))
        {
            if (!Uri.TryCreate(arguments, UriKind.Absolute, out var uri))
                return false;
            normalized = uri.AbsoluteUri;
        }
        else if (Path.IsPathRooted(arguments))
        {
            try
            {
                normalized = Path.GetFullPath(arguments);
            }
            catch
            {
                return false;
            }
        }
        else
        {
            normalized = arguments;
        }

        return effective.Any(p => MatchesPrefix(normalized, p));
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
