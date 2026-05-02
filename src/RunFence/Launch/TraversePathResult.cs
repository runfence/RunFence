namespace RunFence.Launch;

public record TraversePathResult(
    string TraversedPath,
    string? ShortcutArguments,
    string? ShortcutWorkingDirectory,
    bool IsFolder);
