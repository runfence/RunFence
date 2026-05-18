namespace RunFence.Apps.Shortcuts;

public sealed record ShortcutMutation(
    string TargetPath,
    string? Arguments,
    string? WorkingDirectory,
    string? IconLocation,
    ShortcutIconUpdateMode IconUpdateMode,
    string? Description,
    string? Hotkey,
    int WindowStyle);
