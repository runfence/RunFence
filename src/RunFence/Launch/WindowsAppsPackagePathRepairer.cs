namespace RunFence.Launch;

using RunFence.Infrastructure;
using RunFence.Launching.Resolution;

public sealed class WindowsAppsPackagePathRepairer(IBackupIntentFileSystem fileSystem) : IWindowsAppsPackagePathRepairer
{
    public string? TryRepair(string exePath)
    {
        if (!WindowsAppsPackagePathParser.TryParsePackagePath(exePath, out var stalePath))
            return null;

        string? bestPath = null;
        Version? bestVersion = null;
        if (!fileSystem.TryEnumerateDirectories(stalePath.InstallRoot, out var packageDirectories))
            return null;

        foreach (var packageDirectory in packageDirectories)
        {
            var folderName = Path.GetFileName(packageDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            if (!WindowsAppsPackagePathParser.TryParsePackageFolderName(folderName, out var packageName, out var version, out var architecture, out var publisherId))
                continue;

            if (!string.Equals(packageName, stalePath.PackageName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(architecture, stalePath.Architecture, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(publisherId, stalePath.PublisherId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidatePath = Path.Combine(packageDirectory, stalePath.RelativeExecutablePath);
            if (fileSystem.GetFileState(candidatePath) != BackupIntentPathState.Exists)
                continue;

            if (bestVersion == null || version.CompareTo(bestVersion) > 0)
            {
                bestVersion = version;
                bestPath = candidatePath;
            }
        }

        return bestPath;
    }
}
