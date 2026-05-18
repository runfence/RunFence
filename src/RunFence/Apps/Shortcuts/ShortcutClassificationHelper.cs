using System.Runtime.InteropServices;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Apps.Shortcuts;

/// <summary>
/// Pure, stateless helpers for classifying shortcut targets.
/// These methods perform no COM or IO operations.
/// </summary>
public static class ShortcutClassificationHelper
{
    /// <summary>
    /// Extracts the original arguments from a managed shortcut's argument string.
    /// Returns null if the arguments don't match the expected managed format.
    /// </summary>
    public static string? ParseManagedShortcutArgs(string currentArgs, string appId)
    {
        if (!TrySplitCommandLine(currentArgs, out var args) || args.Length == 0)
            return null;

        if (!string.Equals(args[0], appId, StringComparison.Ordinal))
            return null;

        return CommandLineHelper.SkipArgs(currentArgs, 1) ?? "";
    }

    /// <summary>
    /// Returns true if the shortcut targets a folder (directly or via explorer.exe with a folder argument).
    /// </summary>
    public static bool IsFolderShortcutTarget(string? target, string? args, string normalizedFolder)
    {
        if (target == null)
            return false;
        if (TryNormalizePath(target, out var normalizedTarget) &&
            string.Equals(normalizedTarget, normalizedFolder, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!target.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(args))
            return false;

        return TryExtractExplorerPathOperand(args, out var operand) &&
               TryNormalizePath(operand, out var normalizedOperand) &&
               string.Equals(normalizedOperand, normalizedFolder, StringComparison.OrdinalIgnoreCase);
    }

    public static string? TryGetManagedShortcutAppId(string? currentArgs)
        => TrySplitCommandLine(currentArgs, out var args) && args.Length > 0 ? args[0] : null;

    /// <summary>
    /// Returns true if the shortcut name or target exe name suggests an uninstaller.
    /// </summary>
    public static bool IsUninstallShortcut(string shortcutPath, string targetPath)
    {
        var shortcutName = Path.GetFileNameWithoutExtension(shortcutPath);
        var exeName = Path.GetFileNameWithoutExtension(targetPath);
        return shortcutName.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
               exeName.StartsWith("unins", StringComparison.OrdinalIgnoreCase) ||
               exeName.Contains("uninst", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if the target exe resides in the Windows directory or is a known system executable.
    /// </summary>
    public static bool IsSystemExecutable(string targetPath)
    {
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (targetPath.StartsWith(windowsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return true;

        var fileName = Path.GetFileName(targetPath);
        return fileName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase);
    }

    public static List<DiscoveredApp> ExcludeSystemExecutables(IEnumerable<DiscoveredApp> apps)
        => apps.Where(app => !IsSystemExecutable(app.TargetPath)).ToList();

    private static bool TryExtractExplorerPathOperand(string args, out string operand)
    {
        operand = string.Empty;
        if (!TrySplitCommandLine(args, out var parsedArgs))
            return false;

        foreach (var token in parsedArgs)
        {
            if (string.IsNullOrWhiteSpace(token))
                continue;

            if (TryExtractPathCandidate(token, out operand))
                return true;
        }

        return false;
    }

    private static bool TryExtractPathCandidate(string token, out string operand)
    {
        operand = string.Empty;
        var start = FindEmbeddedRootedPathStart(token);
        if (start >= 0)
        {
            var candidate = token[start..].Trim('"');
            if (TryNormalizePath(candidate, out _))
            {
                operand = candidate;
                return true;
            }
        }

        if (!TryNormalizePath(token, out _))
            return false;

        operand = token;
        return true;
    }

    private static int FindEmbeddedRootedPathStart(string token)
    {
        for (var i = 0; i < token.Length; i++)
        {
            if (i + 2 < token.Length &&
                char.IsLetter(token[i]) &&
                token[i + 1] == ':' &&
                (token[i + 2] == Path.DirectorySeparatorChar || token[i + 2] == Path.AltDirectorySeparatorChar))
            {
                return i;
            }

            if (i + 1 < token.Length &&
                token[i] == Path.DirectorySeparatorChar &&
                token[i + 1] == Path.DirectorySeparatorChar)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TrySplitCommandLine(string? commandLine, out string[] args)
    {
        args = [];
        if (string.IsNullOrWhiteSpace(commandLine))
            return false;

        var argv = CommandLineParseNative.CommandLineToArgvW(commandLine, out var argc);
        if (argv == IntPtr.Zero || argc <= 0)
            return false;

        try
        {
            args = new string[argc];
            for (var i = 0; i < argc; i++)
            {
                var valuePtr = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
                args[i] = Marshal.PtrToStringUni(valuePtr) ?? string.Empty;
            }

            return args.Length > 0;
        }
        finally
        {
            CommandLineParseNative.LocalFree(argv);
        }
    }

    private static bool TryNormalizePath(string path, out string normalizedPath)
    {
        try
        {
            normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return true;
        }
        catch
        {
            normalizedPath = string.Empty;
            return false;
        }
    }

    private static class CommandLineParseNative
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

        [DllImport("kernel32.dll")]
        public static extern IntPtr LocalFree(IntPtr handle);
    }
}
