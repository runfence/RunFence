namespace RunFence.Apps.Shortcuts;

public interface IShortcutComHelper
{
    T WithShortcut<T>(string shortcutPath, Func<dynamic, T> action);
    void WithShortcut(string shortcutPath, Action<dynamic> action);
    (string? target, string? args) GetShortcutTargetAndArgs(string shortcutPath);
}
