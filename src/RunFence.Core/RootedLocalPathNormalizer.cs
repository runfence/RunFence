namespace RunFence.Core;

public static class RootedLocalPathNormalizer
{
    public static bool TryNormalizeRootedLocalPath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (!TryNormalizeDriveRootedLocalPath(path, out normalizedPath))
            return false;

        return true;
    }

    public static bool TryNormalizeRootedLocalBoundaryPath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (!TryNormalizeDriveRootedLocalPath(path, out var fullPath))
            return false;

        normalizedPath = TrimEndingSeparatorsPreservingRoot(fullPath);
        return true;
    }

    private static bool TryNormalizeDriveRootedLocalPath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path)
            || !Path.IsPathRooted(path)
            || !IsDriveRootedInput(path)
            || path.StartsWith(@"\\?\", StringComparison.Ordinal)
            || path.StartsWith(@"\\.\", StringComparison.Ordinal)
            || path.StartsWith("//?/", StringComparison.Ordinal)
            || path.StartsWith("//./", StringComparison.Ordinal)
            || path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
            return false;

        try
        {
            normalizedPath = Path.GetFullPath(path);
        }
        catch
        {
            normalizedPath = string.Empty;
            return false;
        }

        return normalizedPath.Length >= 3
               && char.IsLetter(normalizedPath[0])
               && normalizedPath[1] == ':'
               && (normalizedPath[2] == Path.DirectorySeparatorChar || normalizedPath[2] == Path.AltDirectorySeparatorChar);
    }

    private static bool IsDriveRootedInput(string path)
    {
        return path.Length >= 3
               && char.IsLetter(path[0])
               && path[1] == ':'
               && (path[2] == Path.DirectorySeparatorChar || path[2] == Path.AltDirectorySeparatorChar);
    }

    private static string TrimEndingSeparatorsPreservingRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
            return path;

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length < root.Length ? root : trimmed;
    }
}
