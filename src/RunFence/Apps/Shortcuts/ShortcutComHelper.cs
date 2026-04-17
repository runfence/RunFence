using System.Runtime.InteropServices;

namespace RunFence.Apps.Shortcuts;

/// <summary>
/// Low-level COM helpers for reading and writing .lnk shortcut files via WScript.Shell.
/// </summary>
public class ShortcutComHelper : IShortcutComHelper
{
    public T WithShortcut<T>(string shortcutPath, Func<dynamic, T> action)
    {
        dynamic? shell = null;
        dynamic? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
            shell = Activator.CreateInstance(shellType)!;
            shortcut = shell.CreateShortcut(shortcutPath);
            return action(shortcut);
        }
        finally
        {
            if (shortcut != null)
                Marshal.ReleaseComObject(shortcut);
            if (shell != null)
                Marshal.ReleaseComObject(shell);
        }
    }

    public void WithShortcut(string shortcutPath, Action<dynamic> action)
    {
        WithShortcut<object?>(shortcutPath, sc =>
        {
            action(sc);
            return null;
        });
    }

    public (string? target, string? args) GetShortcutTargetAndArgs(string shortcutPath)
    {
        return WithShortcut(shortcutPath, sc => ((string?)sc.TargetPath, (string?)sc.Arguments));
    }

}
