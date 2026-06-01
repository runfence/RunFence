using System;

namespace RunFence.Launch;

public static class LaunchFileExtensionRules
{
    private static readonly HashSet<string> DirectExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".pif", ".cpl"
    };

    private static readonly HashSet<string> CmdScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cmd", ".bat"
    };

    public static bool IsDirectExecutableExtension(string extension)
        => DirectExecutableExtensions.Contains(NormalizeExtension(extension));

    public static bool IsCmdScriptExtension(string extension)
        => CmdScriptExtensions.Contains(NormalizeExtension(extension));

    public static bool IsPowerShellScriptExtension(string extension)
        => string.Equals(NormalizeExtension(extension), ".ps1", StringComparison.OrdinalIgnoreCase);

    public static bool CanLaunchDirectExtension(string extension)
        => IsDirectExecutableExtension(extension);

    public static bool IsSupportedHandlerSuggestionExtension(string extension)
        => IsDirectExecutableExtension(extension)
            || IsCmdScriptExtension(extension)
            || IsPowerShellScriptExtension(extension);

    private static string NormalizeExtension(string extension)
        => string.IsNullOrWhiteSpace(extension) ? string.Empty : extension.Trim();
}
