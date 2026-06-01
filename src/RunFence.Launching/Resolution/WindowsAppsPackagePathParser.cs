namespace RunFence.Launching.Resolution;

public static class WindowsAppsPackagePathParser
{
    public static bool TryParsePackagePath(string exePath, out WindowsAppsPackagePath packagePath)
        => TryParsePackagePathCore(exePath, includeMatchedDirectory: false, out packagePath);

    public static bool IsUnderPackageRoot(string path)
        => TryParsePackagePathCore(path, includeMatchedDirectory: true, out _);

    private static bool TryParsePackagePathCore(
        string exePath,
        bool includeMatchedDirectory,
        out WindowsAppsPackagePath packagePath)
    {
        packagePath = default;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(exePath);
        }
        catch
        {
            return false;
        }

        var currentDirectory = includeMatchedDirectory
            ? fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrEmpty(currentDirectory))
        {
            var installRoot = Path.GetDirectoryName(currentDirectory);
            var installRootName = installRoot == null
                ? string.Empty
                : Path.GetFileName(installRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (installRoot != null
                && string.Equals(installRootName, "WindowsApps", StringComparison.OrdinalIgnoreCase)
                && HasValidPackageInstallRootParent(installRoot)
                && TryParsePackageFolderName(
                    Path.GetFileName(currentDirectory),
                    out var packageName,
                    out var version,
                    out var architecture,
                    out var publisherId))
            {
                var relativePath = Path.GetRelativePath(currentDirectory, fullPath);
                if ((!includeMatchedDirectory && relativePath == ".")
                    || relativePath == ".."
                    || relativePath.StartsWith(@"..\", StringComparison.Ordinal)
                    || relativePath.StartsWith("../", StringComparison.Ordinal)
                    || Path.IsPathRooted(relativePath))
                {
                    return false;
                }

                packagePath = new WindowsAppsPackagePath(
                    installRoot,
                    packageName,
                    version,
                    architecture,
                    publisherId,
                    relativePath);
                return true;
            }

            currentDirectory = installRoot;
        }

        return false;
    }

    public static bool TryParsePackageFolderName(
        string? folderName,
        out string packageName,
        out Version version,
        out string architecture,
        out string publisherId)
    {
        packageName = string.Empty;
        version = new Version();
        architecture = string.Empty;
        publisherId = string.Empty;

        if (string.IsNullOrWhiteSpace(folderName))
            return false;

        var publisherSeparator = folderName.LastIndexOf("__", StringComparison.Ordinal);
        if (publisherSeparator <= 0 || publisherSeparator + 2 >= folderName.Length)
            return false;

        publisherId = folderName[(publisherSeparator + 2)..];
        var beforePublisher = folderName[..publisherSeparator];
        var architectureSeparator = beforePublisher.LastIndexOf('_');
        if (architectureSeparator <= 0 || architectureSeparator + 1 >= beforePublisher.Length)
            return false;

        architecture = beforePublisher[(architectureSeparator + 1)..];
        var beforeArchitecture = beforePublisher[..architectureSeparator];
        var versionSeparator = beforeArchitecture.LastIndexOf('_');
        if (versionSeparator <= 0 || versionSeparator + 1 >= beforeArchitecture.Length)
            return false;

        if (!Version.TryParse(beforeArchitecture[(versionSeparator + 1)..], out var parsedVersion))
            return false;

        packageName = beforeArchitecture[..versionSeparator];
        version = parsedVersion;
        return true;
    }

    private static bool HasValidPackageInstallRootParent(string installRoot)
    {
        var parent = Path.GetDirectoryName(installRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(parent))
            return false;

        var normalizedParent = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parentRoot = Path.GetPathRoot(parent)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(normalizedParent, parentRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        var parentName = Path.GetFileName(parent);
        return string.Equals(parentName, "Program Files", StringComparison.OrdinalIgnoreCase)
               || string.Equals(parentName, "Program Files (x86)", StringComparison.OrdinalIgnoreCase);
    }
}
