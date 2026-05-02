namespace RunFence.Apps.UI.Forms;

/// <summary>Utility methods shared by prefix list controls.</summary>
internal static class PathPrefixHelper
{
    /// <summary>
    /// Normalizes a prefix value: canonicalizes filesystem paths and appends a trailing
    /// backslash. URL scheme prefixes (containing "://") are returned as-is.
    /// </summary>
    public static string NormalizePath(string v)
    {
        if (!v.Contains("://") && Path.IsPathRooted(v))
        {
            try { v = Path.GetFullPath(v); }
            catch { /* leave as-is if path is invalid */ }
            if (!v.EndsWith('\\') && !v.EndsWith('/'))
                return v + "\\";
        }
        return v;
    }
}
