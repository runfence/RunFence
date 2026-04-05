using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Account;

public class AccountSidResolutionService(
    ISidResolver sidResolver,
    ILocalUserProvider localUserProvider) : IAccountSidResolutionService
{
    public async Task<Dictionary<string, string?>> ResolveSidsAsync(CredentialStore credentialStore,
        IReadOnlyDictionary<string, string> sidNames)
    {
        var sidsToResolve = credentialStore.Credentials
            .Where(c => !string.IsNullOrEmpty(c.Sid))
            .Select(c => c.Sid)
            .Concat(sidNames.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return await Task.Run(() =>
        {
            var results = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var sid in sidsToResolve)
                results[sid] = sidResolver.TryResolveName(sid);

            // GetLocalUsers() reads directly from SAM, bypassing the LSA SID-to-name cache
            // which can return stale names after account renames. Override with fresh data.
            try
            {
                foreach (var user in localUserProvider.GetLocalUserAccounts())
                {
                    if (results.ContainsKey(user.Sid))
                        results[user.Sid] = $"{Environment.MachineName}\\{user.Username}";
                }
            }
            catch
            {
                /* local user enumeration may fail in restricted environments */
            }

            return results;
        });
    }

    public Dictionary<Guid, string> BuildDisplayNameCache(CredentialStore credentialStore,
        Dictionary<string, string?> resolutions, IReadOnlyDictionary<string, string>? sidNames)
    {
        var cache = new Dictionary<Guid, string>();
        foreach (var cred in credentialStore.Credentials)
            cache[cred.Id] = BuildDisplayName(cred, resolutions, sidNames);
        return cache;
    }

    private string BuildDisplayName(CredentialEntry cred, Dictionary<string, string?> resolutions,
        IReadOnlyDictionary<string, string>? sidNames)
    {
        string? preResolved = null;
        if (!string.IsNullOrEmpty(cred.Sid))
            resolutions.TryGetValue(cred.Sid, out preResolved);

        if (preResolved != null)
        {
            var username = SidNameResolver.ExtractUsername(preResolved);
            var suffixed = SidNameResolver.ApplyAccountSuffix(username, cred.IsCurrentAccount, cred.IsInteractiveUser);
            if (suffixed != username)
                return suffixed;
            return SidNameResolver.GetDisplayName(cred.Sid, preResolved, sidResolver, sidNames);
        }

        return SidNameResolver.GetDisplayName(cred, sidResolver, sidNames);
    }
}