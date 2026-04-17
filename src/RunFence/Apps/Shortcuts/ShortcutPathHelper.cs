using System.Text;

namespace RunFence.Apps.Shortcuts;

public static class ShortcutPathHelper
{
    public static bool IsSamePath(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string GetLauncherWorkingDirectory(string launcherPath)
        => Path.GetDirectoryName(Path.GetFullPath(launcherPath)) ?? AppContext.BaseDirectory;

    /// <summary>
    /// Replaces any character that is invalid in a file name with an underscore.
    /// </summary>
    public static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(invalidChars, c) < 0 ? c : '_');
        return sb.ToString();
    }
}
