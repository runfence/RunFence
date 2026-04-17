using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Account;

/// <summary>
/// Handles credential store and config save operations, and stale SID name updates.
/// Extracted from <see cref="AccountCredentialManager"/> to separate persistence concerns
/// from credential CRUD operations.
/// </summary>
public class SessionPersistenceHelper(
    ICredentialRepository credentialRepository,
    IConfigRepository configRepository,
    ISidNameCacheService sidNameCache,
    ILoggingService log)
{
    public void SaveCredentialStoreAndConfig(
        CredentialStore credStore, AppDatabase database, ProtectedBuffer pinKey)
    {
        using var scope = pinKey.Unprotect();
        credentialRepository.SaveCredentialStoreAndConfig(credStore, database, scope.Data);
    }

    public void SaveConfig(AppDatabase database, ProtectedBuffer pinKey, byte[] argonSalt)
    {
        using var scope = pinKey.Unprotect();
        configRepository.SaveConfig(database, scope.Data, argonSalt);
    }

    /// <summary>
    /// Detects and applies stale name updates from pre-resolved SID→name mappings.
    /// Updates AppDatabase.SidNames (config only — not credential store).
    /// Returns true if any names were updated (and saved).
    /// </summary>
    public bool ApplyStaleNameUpdates(
        Dictionary<string, string?> resolutions,
        AppDatabase database, ProtectedBuffer pinKey, byte[] argonSalt)
    {
        bool changed = false;
        foreach (var (sid, name) in resolutions)
        {
            if (string.IsNullOrEmpty(sid) || name == null)
                continue;

            if (!database.SidNames.TryGetValue(sid, out var existing) ||
                !string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
            {
                log.Info($"Stale name detected: '{existing}' -> '{name}' for SID {sid}");
                sidNameCache.UpdateName(sid, name);
                changed = true;
            }
        }

        if (changed)
            SaveConfig(database, pinKey, argonSalt);

        return changed;
    }
}
