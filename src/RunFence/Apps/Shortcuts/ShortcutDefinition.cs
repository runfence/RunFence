namespace RunFence.Apps.Shortcuts;

public sealed record ShortcutDefinition(
    string Path,
    string? TargetPath,
    string? Arguments,
    string? WorkingDirectory);
