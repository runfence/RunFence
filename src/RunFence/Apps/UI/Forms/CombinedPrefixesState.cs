namespace RunFence.Apps.UI.Forms;

public sealed record CombinedPrefixesState(
    IReadOnlyList<string>? AppPrefixes,
    IReadOnlyList<string>? AssociationPrefixes,
    bool ReplacePrefixes);
