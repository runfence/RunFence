namespace RunFence.Apps.Shortcuts;

public record struct ShortcutInfo(string? Target, string? Arguments, string? WorkingDirectory);

public interface IShortcutComHelper
{
    T WithShortcut<T>(string shortcutPath, Func<dynamic, T> action);
    void WithShortcut(string shortcutPath, Action<dynamic> action);
    ShortcutInfo GetShortcutTargetAndArgs(string shortcutPath);
}
