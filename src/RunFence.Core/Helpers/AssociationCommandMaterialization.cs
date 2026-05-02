namespace RunFence.Core.Helpers;

public sealed record AssociationCommandMaterialization(
    string ExpandedCommand,
    string MaterializedCommand,
    string ExePath,
    string? Arguments,
    bool UsedSupportedPlaceholder);
