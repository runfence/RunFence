using RunFence.Core.Models;

namespace RunFence.Ipc;

public interface IIpcCallerAuthorizer
{
    bool IsCallerAuthorizedGlobal(string? callerIdentity, string? callerSid, AppDatabase database);
    bool IsCallerAuthorized(string? callerIdentity, string? callerSid, AppEntry app, AppDatabase database);

    /// <summary>
    /// Authorizes a caller for handler association launches using the standard per-app/global IPC caller
    /// rules, but always includes the interactive user SID in the effective allowed list.
    /// This is by design: the interactive user registered these associations in their own registry hive
    /// via Windows Settings, so they must always be able to invoke them. Other callers still go through
    /// standard IPC authorization and may succeed if the per-app or global rules allow them.
    /// </summary>
    bool IsCallerAuthorizedForAssociation(string? callerIdentity, string? callerSid, AppEntry app, AppDatabase database);

    bool AuthorizeAgainstList(string? callerIdentity, string? callerSid,
        IEnumerable<string> allowedList, string logContext, IReadOnlyDictionary<string, string>? sidNames = null);

    bool MatchesCaller(string callerIdentity, string? callerSid, string allowedSid,
        IReadOnlyDictionary<string, string>? sidNames = null);

    bool MatchesCaller(string callerIdentity, string allowedSid,
        IReadOnlyDictionary<string, string>? sidNames = null);
}