using System.Runtime.InteropServices;

namespace PrefTrans.Services.IO;

public class WshPinnedShortcutReader : IPinnedShortcutReader
{
    public string? ReadTargetPath(string shortcutPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
            return null;

        dynamic? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell == null)
                return null;

            dynamic? shortcut = null;
            try
            {
                shortcut = shell.CreateShortcut(shortcutPath);
                return shortcut?.TargetPath as string;
            }
            finally
            {
                if (shortcut != null)
                    Marshal.ReleaseComObject(shortcut);
            }
        }
        finally
        {
            if (shell != null)
                Marshal.ReleaseComObject(shell);
        }
    }
}
