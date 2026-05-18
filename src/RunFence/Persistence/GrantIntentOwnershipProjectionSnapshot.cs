using RunFence.Core.Models;

namespace RunFence.Persistence;

internal sealed record GrantIntentOwnershipProjectionSnapshot(
    IReadOnlySet<GrantIntentEntryIdentity> MainOwnership,
    IReadOnlyDictionary<string, IReadOnlySet<GrantIntentEntryIdentity>> AdditionalOwnership,
    IReadOnlyDictionary<string, IReadOnlyDictionary<GrantIntentEntryIdentity, GrantedPathEntry>> AdditionalProjectionEntries,
    bool MainOwnershipCaptured)
{
    public static readonly GrantIntentOwnershipProjectionSnapshot Empty = new(
        new HashSet<GrantIntentEntryIdentity>(),
        new Dictionary<string, IReadOnlySet<GrantIntentEntryIdentity>>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, IReadOnlyDictionary<GrantIntentEntryIdentity, GrantedPathEntry>>(StringComparer.OrdinalIgnoreCase),
        false);
}
