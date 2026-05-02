namespace RunFence.Apps.UI;

public record struct HandlerAssociationItem(
    string Key,
    string? ArgumentsTemplate,
    IReadOnlyList<string>? PathPrefixes = null,
    bool ReplacePrefixes = false);
