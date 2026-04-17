using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Account;

/// <summary>
/// Filters credential entries to only those whose SIDs can be resolved to a username,
/// exist in the SidNames map, or represent the current/interactive user account.
/// Used by dialog combo boxes to hide stale/orphaned credentials.
/// </summary>
public class CredentialFilterHelper(ISidResolver sidResolver)
{
    /// <summary>
    /// Returns credentials that are resolvable: current account, interactive user,
    /// resolvable via OS lookup, present in <paramref name="sidNames"/>, or matching
    /// the <paramref name="existing"/> app's account SID (if provided).
    /// </summary>
    public List<CredentialEntry> FilterResolvableCredentials(
        IEnumerable<CredentialEntry> credentials,
        IReadOnlyDictionary<string, string>? sidNames,
        AppEntry? existing = null)
    {
        return credentials.Where(cred =>
        {
            bool isCurrentUser = cred.IsCurrentAccount || cred.IsInteractiveUser;
            bool hasValidSid = sidResolver.TryResolveName(cred.Sid) != null;
            bool isResolvable = (sidNames != null && sidNames.ContainsKey(cred.Sid)) ||
                                (existing != null && string.Equals(existing.AccountSid, cred.Sid, StringComparison.OrdinalIgnoreCase));
            return isCurrentUser || hasValidSid || isResolvable;
        }).ToList();
    }
}
