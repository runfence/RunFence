namespace RunFence.Apps.Shortcuts;

public sealed class ShortcutProtectionException(string shortcutPath, string operation, Exception innerException)
    : Exception($"Failed to {operation} shortcut protection for '{shortcutPath}'.", innerException)
{
    public string ShortcutPath { get; } = shortcutPath;
    public string Operation { get; } = operation;
}
