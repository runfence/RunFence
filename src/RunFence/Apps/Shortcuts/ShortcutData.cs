namespace RunFence.Apps.Shortcuts;

public sealed record ShortcutData(
    string TargetPath,
    string? Arguments,
    string? WorkingDirectory,
    string? IconPath,
    int IconIndex,
    string? Description,
    short Hotkey,
    int WindowStyle);
