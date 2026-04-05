using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.SidMigration.UI;

/// <summary>
/// Handles data/SID resolution for SID mapping: builds mapping dictionaries and resolves display names.
/// No WinForms dependencies. Injected into <see cref="SidMigrationMappingBuilder"/>.
/// </summary>
public class SidMigrationMappingLogic(
    SessionContext session,
    ISidMigrationService sidMigrationService,
    ILocalUserProvider localUserProvider,
    IEnumerable<OrphanedSid> orphanedSids,
    ISidResolver sidResolver,
    ISidNameCacheService sidNameCache)
{
    public async Task<Dictionary<string, (string guessedName, string? newSid)>> BuildMappingsAsync()
    {
        var credentials = session.CredentialStore.Credentials.ToList();
        var localUsers = localUserProvider.GetLocalUserAccounts().ToList();
        var discoveredSids = orphanedSids.ToList();
        var sidNames = session.Database.SidNames;
        var discoveryDone = discoveredSids.Count > 0;

        return await Task.Run(() =>
        {
            var credentialMappings = sidMigrationService.BuildMappings(credentials, localUsers, sidNames);

            var result = new Dictionary<string, (string, string?)>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in credentialMappings)
                result[m.OldSid] = (m.Username, m.NewSid);

            if (discoveryDone)
            {
                foreach (var orphan in discoveredSids)
                {
                    if (!result.ContainsKey(orphan.Sid))
                    {
                        var name = orphan.GuessedName
                                   ?? (sidNames != null && sidNames.TryGetValue(orphan.Sid, out var mapName) ? mapName : null)
                                   ?? "(unknown)";
                        result[orphan.Sid] = (name, null);
                    }
                }
            }

            return result;
        });
    }

    public Dictionary<string, string> BuildSidDisplayNames()
    {
        var sidDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in localUserProvider.GetLocalUserAccounts())
        {
            var displayName = sidNameCache.GetDisplayName(user.Sid);
            sidDisplayNames[user.Sid] = $"{displayName} ({user.Sid})";
        }

        return sidDisplayNames;
    }

    public IEnumerable<string> GetLocalUserSids() =>
        localUserProvider.GetLocalUserAccounts().Select(u => u.Sid);

    public string ResolveDisplayNameForUnknown(string oldSid) =>
        sidResolver.TryResolveNameFromRegistry(oldSid) is { } regName
            ? $"{regName} ({oldSid})"
            : "(unknown)";
}