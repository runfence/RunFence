using System.Text.RegularExpressions;

namespace RunFence.Core;

public static class PathHelper
{
    /// <summary>
    /// Returns true if the path looks like a URL scheme (e.g., "https://..." or "myapp:..."),
    /// as opposed to a drive letter path (e.g., "C:\...") or UNC path (e.g., "\\server\...").
    /// </summary>
    public static bool IsUrlScheme(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var colonIndex = path.IndexOf(':');
        if (colonIndex <= 1)
            return false; // Single letter = drive letter
        return !path.StartsWith(@"\\"); // Not UNC
    }

    // Allowlist of characters safe for embedding in cmd.exe batch scripts.
    // Excludes cmd metacharacters: & | " % ^ < > !
    private static readonly Regex SafeCmdPathRegex = new(
        @"^[a-zA-Z0-9_\-. :\\()\+@#\[\]\{\}~',]+$", RegexOptions.Compiled);

    /// <summary>
    /// Returns true if the given path is under or equal to a blocked ACL path
    /// (e.g., Windows directory, system drive root, Program Files).
    /// Used for deny-mode ACL operations where modifying anything under a system root is dangerous.
    /// </summary>
    public static bool IsBlockedAclPath(string path)
    {
        var blockedPaths = Constants.GetBlockedAclPaths();
        return blockedPaths.Any(bp =>
            path.StartsWith(bp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.Equals(path, bp, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns true if the given path IS a blocked ACL root itself (exact match only).
    /// Children of blocked roots are NOT blocked. Used for allow-mode permission grants
    /// where modifying a specific file under C:\Users is safe, but modifying C:\Users itself is not.
    /// </summary>
    public static bool IsBlockedAclRoot(string path)
    {
        var blockedPaths = Constants.GetBlockedAclPaths();
        return blockedPaths.Any(bp => string.Equals(path, bp, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves a potentially-relative file path to an absolute path by combining with the
    /// application's base directory. Returns the original value when null/empty or already rooted.
    /// Always returns the resolved path for relative inputs (regardless of whether the file exists),
    /// ensuring consistent launch behavior when WorkingDirectory differs from BaseDirectory.
    /// </summary>
    public static string ResolveExePath(string exePath)
    {
        if (string.IsNullOrEmpty(exePath) || Path.IsPathRooted(exePath))
            return exePath;
        return Path.Combine(AppContext.BaseDirectory, exePath);
    }

    /// <summary>
    /// Returns true if a path contains only characters safe for use in cmd.exe batch scripts.
    /// Rejects UNC paths, empty paths, and paths containing cmd metacharacters.
    /// </summary>
    public static bool IsPathSafeForCmd(string path)
    {
        if (string.IsNullOrEmpty(path) || path.StartsWith(@"\\"))
            return false;

        return SafeCmdPathRegex.IsMatch(path);
    }

    /// <summary>
    /// Returns true if the path contains characters that cannot be safely escaped for cmd.exe:
    /// double quotes, percent signs (trigger variable expansion even with ^%), and control characters.
    /// Other cmd metacharacters (^, &amp;, |, &lt;, &gt;, !, (, )) can be escaped with ^ and should be handled by caller.
    /// </summary>
    public static bool ContainsCmdUnescapableChars(string path)
    {
        foreach (var c in path)
        {
            if (c is '"' or '%')
                return true;
            if (c <= 0x1F || c == 0x7F)
                return true;
        }

        return false;
    }
}