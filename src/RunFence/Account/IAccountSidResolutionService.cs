using RunFence.Core.Models;

namespace RunFence.Account;

public interface IAccountSidResolutionService
{
    /// <summary>
    /// Resolves all SIDs from the credential store and database SID names map.
    /// Overrides cached names with fresh data from the SAM database.
    /// Returns a SID → resolved name mapping (value may be null if not resolvable).
    /// </summary>
    Task<Dictionary<string, string?>> ResolveSidsAsync(CredentialStore credentialStore,
        IReadOnlyDictionary<string, string> sidNames);

    /// <summary>
    /// Builds a display name cache for all credentials using pre-resolved SID names.
    /// Returns a credential ID → display name mapping.
    /// </summary>
    Dictionary<Guid, string> BuildDisplayNameCache(CredentialStore credentialStore,
        Dictionary<string, string?> resolutions, IReadOnlyDictionary<string, string>? sidNames);
}