namespace RunFence.Apps.Shortcuts;

internal readonly record struct ShortcutWriteResult(string Path, string? TargetPath, string? Arguments);
