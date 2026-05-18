using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Account.UI;

public sealed class CredentialDisplayItemFactory(
    ISidResolver sidResolver,
    IProfilePathResolver profilePathResolver)
{
    public CredentialDisplayItem Create(
        CredentialEntry credential,
        IReadOnlyDictionary<string, string>? sidNames,
        bool isEphemeral = false,
        bool hasStoredCredential = true)
        => new CredentialDisplayItem(credential, sidResolver, profilePathResolver, sidNames,
            hasStoredCredential: hasStoredCredential, isEphemeral: isEphemeral);

    public CredentialDisplayItem Create(
        IEnumerable<CredentialEntry> credentials,
        string sid,
        IReadOnlyDictionary<string, string>? sidNames,
        bool isEphemeral = false,
        bool? hasStoredCredentialOverride = null)
    {
        if (string.IsNullOrWhiteSpace(sid))
            throw new ArgumentException("SID cannot be null or whitespace.", nameof(sid));

        var existingCredential = credentials.FirstOrDefault(
            c => string.Equals(c.Sid, sid, StringComparison.OrdinalIgnoreCase));
        var hasStoredCredential = hasStoredCredentialOverride ?? existingCredential != null;

        var credential = existingCredential ?? new CredentialEntry { Id = Guid.NewGuid(), Sid = sid };
        return new CredentialDisplayItem(
            credential,
            sidResolver,
            profilePathResolver,
            sidNames,
            hasStoredCredential: hasStoredCredential,
            isEphemeral: isEphemeral);
    }
}
