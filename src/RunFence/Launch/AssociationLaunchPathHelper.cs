namespace RunFence.Launch;

internal static class AssociationLaunchPathHelper
{
    public static bool IsUnderUsersRoot(string exePath)
    {
        if (!Path.IsPathRooted(exePath))
            return false;

        var usersRoot = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (string.IsNullOrEmpty(usersRoot))
            return false;

        var normalizedUsersRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(usersRoot));
        var normalizedExePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(exePath));

        return normalizedExePath.StartsWith(
            normalizedUsersRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }
}
