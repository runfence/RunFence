namespace PrefTrans.Services;

public class UserProfileFilter : IUserProfileFilter
{
    public string[] GetUserProfilePaths()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfile))
            return [];

        // Also match entries that use %SystemDrive%\Users\username instead of C:\Users\username
        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        var userNamePart = userProfile[systemDrive.Length..]; // e.g. \Users\JohnDoe
        var systemDriveVariant = "%SystemDrive%" + userNamePart;

        return [userProfile, systemDriveVariant];
    }

    public bool ContainsUserProfilePath(string? value, string[] profilePaths)
    {
        if (string.IsNullOrEmpty(value) || profilePaths.Length == 0)
            return false;

        foreach (var profilePath in profilePaths)
        {
            int startIdx = 0;
            while (startIdx < value.Length)
            {
                int idx = value.IndexOf(profilePath, startIdx, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    break;

                int endIdx = idx + profilePath.Length;
                if (endIdx >= value.Length)
                    return true; // value ends with the profile path

                char next = value[endIdx];
                if (next == '\\' || next == '/' || next == '"' || char.IsWhiteSpace(next))
                    return true; // proper boundary

                // Not a boundary match (e.g. C:\Users\foo inside C:\Users\foobar) — keep searching
                startIdx = endIdx;
            }
        }

        return false;
    }

    // Returns true if value references the Windows UWP package store directory.
    // C:\Program Files\WindowsApps\ is owned by TrustedInstaller and cannot be
    // executed by arbitrary user accounts. Per-user execution aliases under
    // %LOCALAPPDATA%\Microsoft\WindowsApps\ are already caught by ContainsUserProfilePath.
    public bool ContainsWindowsAppsPath(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;
        return value.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
    }

    // Returns true if progId is a UWP/Store app identifier.
    // AppX<hex> ProgIds are system-hashed package identities; AUMIDs use the
    // PackageFamilyName!AppId format with '!' as the separator. Both are
    // user-specific package registrations that cannot be transferred between accounts.
    public bool IsUwpProgId(string? progId)
    {
        if (string.IsNullOrEmpty(progId))
            return false;
        return progId.StartsWith("AppX", StringComparison.OrdinalIgnoreCase)
               || progId.Contains('!');
    }
}