using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Ipc;

public class IpcCallerAuthorizer(ILoggingService log, ISidResolver sidResolver) : IIpcCallerAuthorizer
{
    public bool IsCallerAuthorizedGlobal(string? callerIdentity, string? callerSid, AppDatabase database)
    {
        var globalList = database.Accounts.Where(a => a.IsIpcCaller).Select(a => a.Sid);

        // Global empty list = unrestricted
        if (!globalList.Any())
            return true;

        return AuthorizeAgainstList(callerIdentity, callerSid, globalList, "RunAs", database.SidNames);
    }

    public bool IsCallerAuthorized(string? callerIdentity, string? callerSid, AppEntry app, AppDatabase database)
    {
        // Per-app list (non-null) overrides global; null = inherit global
        var hasPerAppOverride = app.AllowedIpcCallers != null;
        var effectiveList = (IEnumerable<string>?)app.AllowedIpcCallers
                            ?? database.Accounts.Where(a => a.IsIpcCaller).Select(a => a.Sid);

        // Global empty list = unrestricted; per-app empty list = block all
        if (!hasPerAppOverride && !effectiveList.Any())
            return true;

        return AuthorizeAgainstList(callerIdentity, callerSid, effectiveList, app.Name, database.SidNames);
    }

    public bool IsCallerAuthorizedForAssociation(string? callerIdentity, string? callerSid, AppEntry app, AppDatabase database)
    {
        // Standard per-app/global authorization
        var hasPerAppOverride = app.AllowedIpcCallers != null;
        var effectiveList = (IEnumerable<string>?)app.AllowedIpcCallers
                            ?? database.Accounts.Where(a => a.IsIpcCaller).Select(a => a.Sid);

        // Global empty list = unrestricted (before interactive user augmentation)
        if (!hasPerAppOverride && !effectiveList.Any())
            return true;

        // Always include interactive user SID for handler associations:
        // The interactive user registered these associations in their own registry hive via Windows Settings,
        // so they must always be able to invoke them. This does not modify the app entry itself.
        var interactiveUserSid = SidResolutionHelper.GetInteractiveUserSid();
        IEnumerable<string> augmentedList;
        if (interactiveUserSid != null)
        {
            var materialized = effectiveList.ToList();
            if (!materialized.Contains(interactiveUserSid, StringComparer.OrdinalIgnoreCase))
                materialized.Add(interactiveUserSid);
            augmentedList = materialized;
        }
        else
        {
            augmentedList = effectiveList;
        }

        return AuthorizeAgainstList(callerIdentity, callerSid, augmentedList, app.Name, database.SidNames);
    }

    public bool AuthorizeAgainstList(string? callerIdentity, string? callerSid,
        IEnumerable<string> allowedList, string logContext, IReadOnlyDictionary<string, string>? sidNames = null)
    {
        if (string.IsNullOrEmpty(callerIdentity) && string.IsNullOrEmpty(callerSid))
        {
            log.Warn($"IPC caller identity unknown, denying access to {logContext}");
            return false;
        }

        var resolvedSid = callerSid ?? sidResolver.TryResolveSid(callerIdentity!);

        if (resolvedSid == null)
            log.Warn($"SID resolution failed for IPC caller '{callerIdentity}', falling back to name-based matching (weaker security)");

        if (allowedList.Any(allowedSid => MatchesCaller(callerIdentity ?? "", resolvedSid, allowedSid, sidNames)))
            return true;

        log.Warn($"IPC caller '{callerIdentity}' not authorized for {logContext}");
        return false;
    }

    public bool MatchesCaller(string callerIdentity, string allowedSid,
        IReadOnlyDictionary<string, string>? sidNames = null)
    {
        var callerSid = sidResolver.TryResolveSid(callerIdentity);
        return MatchesCaller(callerIdentity, callerSid, allowedSid, sidNames);
    }

    public bool MatchesCaller(string callerIdentity, string? callerSid, string allowedSid,
        IReadOnlyDictionary<string, string>? sidNames = null)
    {
        // SID-based match
        if (callerSid != null && string.Equals(callerSid, allowedSid, StringComparison.OrdinalIgnoreCase))
            return true;

        // Fallback: name-based match using SidNames map if SID resolution fails.
        // WARNING: This is a weaker check — username-only matching can match across domains.
        // SID-based matching should be preferred; this exists only for resilience when DC is unreachable.
        if (callerSid == null)
        {
            var allowedName = sidNames != null && sidNames.TryGetValue(allowedSid, out var n) ? n : null;
            if (allowedName != null)
            {
                if (string.Equals(callerIdentity, allowedName, StringComparison.OrdinalIgnoreCase))
                    return true;

                // Username-only match (cross-domain risk: "Admin" matches ANYDOMAIN\Admin)
                if (!allowedName.Contains('\\'))
                {
                    var backslashIndex = callerIdentity.IndexOf('\\');
                    if (backslashIndex >= 0)
                    {
                        var callerUsername = callerIdentity[(backslashIndex + 1)..];
                        if (string.Equals(callerUsername, allowedName, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
        }

        return false;
    }
}