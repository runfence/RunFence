using System.ComponentModel;
using RunFence.Infrastructure;

namespace RunFence.ForegroundMarker;

public sealed class ForegroundShellWindowFilter(IProcessImagePathReader processImagePathReader)
{
    private static readonly HashSet<string> ShellProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SearchHost.exe",
        "ShellExperienceHost.exe",
        "StartMenuExperienceHost.exe",
        "TextInputHost.exe",
    };

    public bool IsShellWindow(ForegroundWindowInfo foregroundWindow)
    {
        var processPath = TryGetProcessPath(foregroundWindow.ProcessId);
        if (string.IsNullOrWhiteSpace(processPath))
            return false;

        var executableName = Path.GetFileName(processPath);
        return !string.IsNullOrWhiteSpace(executableName)
               && ShellProcessNames.Contains(executableName)
               && IsWindowsSystemAppsPath(processPath);
    }

    private static bool IsWindowsSystemAppsPath(string processPath)
    {
        var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsPath))
            return false;

        try
        {
            var systemAppsPath = Path.Join(windowsPath, "SystemApps");
            var fullProcessPath = Path.GetFullPath(processPath);
            var fullSystemAppsPath = Path.GetFullPath(systemAppsPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            return fullProcessPath.StartsWith(fullSystemAppsPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private string? TryGetProcessPath(uint processId)
    {
        if (processId == 0)
            return null;

        try
        {
            return processImagePathReader.TryGetProcessImagePath(processId);
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
