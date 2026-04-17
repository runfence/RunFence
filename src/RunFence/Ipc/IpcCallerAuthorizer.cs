using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Ipc;

public class IpcCallerAuthorizer(ILoggingService log, ISidResolver sidResolver) : IIpcCallerAuthorizer
{
    public bool IsCallerAuthorizedGlobal(string? callerIdentity, string? callerSid, AppDatabase database, bool identityFromImpersonation = true)
    {
        var callerList = GetEffectiveCallerList(null, database);

        // Global empty list = unrestricted
        if (callerList.Count == 0)
            return true;

        return AuthorizeAgainstList(callerIdentity, callerSid, callerList, "RunAs", database.SidNames, identityFromImpersonation);
    }

    public bool IsCallerAuthorized(string? callerIdentity, string? callerSid, AppEntry app, AppDatabase database, bool identityFromImpersonation)
    {
        var callerList = GetEffectiveCallerList(app, database);

        // Global empty list = unrestricted; per-app empty list = block all
        if (app.AllowedIpcCallers == null && callerList.Count == 0)
            return true;

        return AuthorizeAgainstList(callerIdentity, callerSid, callerList, app.Name, database.SidNames, identityFromImpersonation);
    }

    public bool IsCallerAuthorizedForAssociation(string? callerIdentity, string? callerSid, AppEntry app, AppDatabase database, bool identityFromImpersonation)
    {
        var callerList = GetEffectiveCallerList(app, database);

        // Global empty list = unrestricted (before interactive user augmentation)
        if (app.AllowedIpcCallers == null && callerList.Count == 0)
            return true;

        // Always include interactive user SID for handler associations:
        // The interactive user registered these associations in their own registry hive via Windows Settings,
        // so they must always be able to invoke them. This does not modify the app entry itself.
        var interactiveUserSid = SidResolutionHelper.GetInteractiveUserSid();
        if (interactiveUserSid != null && !callerList.Contains(interactiveUserSid, StringComparer.OrdinalIgnoreCase))
            callerList.Add(interactiveUserSid);

        return AuthorizeAgainstList(callerIdentity, callerSid, callerList, app.Name, database.SidNames, identityFromImpersonation);
    }

    public bool HasExplicitPerAppAuthorization(string? callerSid, AppEntry app, AppDatabase database)
        => app.AllowedIpcCallers != null
           && callerSid != null
           && app.AllowedIpcCallers.Any(s => string.Equals(s, callerSid, StringComparison.OrdinalIgnoreCase));

    private List<string> GetEffectiveCallerList(AppEntry? app, AppDatabase database)
    {
        if (app?.AllowedIpcCallers != null)
            return app.AllowedIpcCallers.ToList();
        return database.Accounts.Where(a => a.IsIpcCaller).Select(a => a.Sid).ToList();
    }

    public bool AuthorizeAgainstList(string? callerIdentity, string? callerSid,
        IEnumerable<string> allowedList, string logContext, IReadOnlyDictionary<string, string>? sidNames = null,
        bool identityFromImpersonation = true)
    {
        if (string.IsNullOrEmpty(callerIdentity) && string.IsNullOrEmpty(callerSid))
        {
            log.Warn($"IPC caller identity unknown, denying access to {logContext}");
            return false;
        }

        // CallerName fallback spoofing protection: when caller SID was not obtained via pipe
        // impersonation (RunAsClient), do not rely on the caller-supplied identity for authorization.
        // A caller with no verifiable SID could forge their CallerName field in the IPC message
        // to impersonate another user. Impersonation is the only tamper-evident identity source.
        if (callerSid == null && !identityFromImpersonation)
        {
            log.Warn($"IPC caller SID unavailable and identity not from impersonation, denying access to {logContext}");
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