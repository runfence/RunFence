using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Account.UI;

public class CredentialDisplayItem
{
    public CredentialEntry Credential { get; }
    public bool HasStoredCredential { get; }
    private readonly string _displayName;

    public CredentialDisplayItem(CredentialEntry credential, ISidResolver sidResolver,
        IReadOnlyDictionary<string, string>? sidNames = null,
        bool hasStoredCredential = true, bool isEphemeral = false)
    {
        Credential = credential;
        HasStoredCredential = hasStoredCredential;
        var baseName = SidNameResolver.GetDisplayName(credential, sidResolver, sidNames);
        _displayName = isEphemeral ? $"{baseName} (ephemeral)" : baseName;
    }

    public override string ToString() => _displayName;
}