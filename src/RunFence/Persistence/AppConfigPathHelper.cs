namespace RunFence.Persistence;

internal static class AppConfigPathHelper
{
    public static string NormalizePath(string path)
        => Path.GetFullPath(path);

    public static string NormalizeDirectory(string path)
    {
        var normalized = Path.GetFullPath(path);
        return normalized.EndsWith(Path.DirectorySeparatorChar)
            ? normalized
            : normalized + Path.DirectorySeparatorChar;
    }
}
