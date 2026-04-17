using System.Text;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Launch;

/// <summary>
/// Static helpers for process launch argument/working-directory resolution, URL validation,
/// cmd.exe metacharacter escaping, and target wrapping. Shared between callers in the launch pipeline.
/// </summary>
public static class ProcessLaunchHelper
{
    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".cmd", ".bat" };

    private static readonly HashSet<string> ExeExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".com", ".scr", ".pif", ".cpl" };

    /// <summary>
    /// Wraps the target for launch when the target is not a native executable.
    /// Scripts (.cmd, .bat) are wrapped with <c>cmd.exe /c</c>; other non-exe types are wrapped
    /// with <c>rundll32.exe shell32.dll,ShellExec_RunDLL</c>. Native executables are returned as-is.
    /// </summary>
    /// <returns>
    /// The (possibly wrapped) target and a flag indicating whether the launched process result
    /// should be discarded — true when the target was wrapped with ShellExec_RunDLL (non-exe,
    /// non-script files), false for native executables and scripts.
    /// </returns>
    public static (ProcessLaunchTarget Target, bool IsWrapped) WrapTargetForLaunch(ProcessLaunchTarget target)
    {
        var ext = Path.GetExtension(target.ExePath);
        var isExe = ExeExtensions.Contains(ext);
        if (isExe)
            return (target, false);

        var filePath = target.ExePath;

        var workingDirectory = string.IsNullOrEmpty(target.WorkingDirectory)
            ? Path.GetDirectoryName(filePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            : target.WorkingDirectory;

        if (ScriptExtensions.Contains(ext))
        {
            if (!PathHelper.IsPathSafeForCmd(filePath))
                throw new InvalidOperationException("File path contains characters unsafe for cmd.exe execution.");

            string argsString;
            if (string.IsNullOrEmpty(target.Arguments))
                argsString = "";
            else
            {
                if (PathHelper.ContainsCmdUnescapableChars(target.Arguments))
                    throw new InvalidOperationException("Arguments contain characters unsafe for cmd.exe execution.");
                argsString = " " + EscapeCmdMetacharacters(target.Arguments);
            }

            return (new ProcessLaunchTarget(
                ExePath: "cmd.exe",
                Arguments: $"/c \"{filePath}\"{argsString}",
                HideWindow: target.HideWindow,
                WorkingDirectory: workingDirectory,
                EnvironmentVariables: target.EnvironmentVariables
            ), false);
        }

        if (string.Equals(ext, ".ps1", StringComparison.OrdinalIgnoreCase))
        {
            // PowerShell is launched directly via CreateProcess — not through cmd.exe.
            // Arguments are parsed by PowerShell's own parser; cmd.exe escaping/validation must not be applied.
            var argsString = string.IsNullOrEmpty(target.Arguments) ? "" : " " + target.Arguments;

            return (new ProcessLaunchTarget(
                ExePath: "powershell.exe",
                Arguments: $"-ExecutionPolicy Bypass -File \"{filePath}\"{argsString}",
                HideWindow: target.HideWindow,
                WorkingDirectory: workingDirectory,
                EnvironmentVariables: target.EnvironmentVariables
            ), false);
        }

        // Non-exe, non-script: use rundll32 shell32.dll,ShellExec_RunDLL.
        // Calls ShellExecuteEx internally — no file open, so locked files work fine.
        // lpCmdLine is passed verbatim as lpFile; no quoting needed or supported.
        return (new ProcessLaunchTarget(
            ExePath: "rundll32.exe",
            Arguments: "shell32.dll,ShellExec_RunDLL " + filePath,
            WorkingDirectory: workingDirectory,
            EnvironmentVariables: target.EnvironmentVariables
        ), true);
    }

    /// <summary>
    /// Validates the URL scheme and returns a <see cref="ProcessLaunchTarget"/> that launches the
    /// URL via <c>rundll32.exe url.dll,FileProtocolHandler</c>.
    /// Throws <see cref="InvalidOperationException"/> when the scheme is blocked or the URL is malformed.
    /// </summary>
    public static ProcessLaunchTarget BuildUrlLaunchTarget(string url)
    {
        if (!ValidateUrlScheme(url, out var error))
            throw new InvalidOperationException($"URL scheme blocked: {error}");

        // Use rundll32.exe url.dll,FileProtocolHandler instead of cmd.exe /c start.
        // rundll32 passes everything after the entry point verbatim to ShellExecuteEx —
        // no shell expansion, so %, &, spaces, etc. are all safe without escaping.
        return new ProcessLaunchTarget
        (
            ExePath: "rundll32.exe",
            Arguments: "url.dll,FileProtocolHandler " + url
        );
    }

    /// <summary>
    /// Returns the raw argument string to use for a launch.
    /// <para>
    /// When <see cref="AppEntry.AllowPassingArguments"/> is true and a non-empty
    /// <paramref name="launcherArguments"/> string is provided:
    /// <list type="bullet">
    ///   <item>If <see cref="AppEntry.ArgumentsTemplate"/> contains <c>%1</c>: passed args (MSVC CRT-safe
    ///   escaped) replace all <c>%1</c> placeholders in the template.</item>
    ///   <item>If <see cref="AppEntry.ArgumentsTemplate"/> has no <c>%1</c>: passed args are appended after
    ///   the template value.</item>
    ///   <item>If <see cref="AppEntry.ArgumentsTemplate"/> is null/empty: passed args replace
    ///   <see cref="AppEntry.DefaultArguments"/> entirely (original behavior).</item>
    /// </list>
    /// When no launcher arguments are passed, <see cref="AppEntry.DefaultArguments"/> is returned as-is.
    /// </para>
    /// </summary>
    public static string? DetermineArguments(AppEntry app, string? launcherArguments, string? associationArgsTemplate = null)
    {
        if (!app.AllowPassingArguments)
            return string.IsNullOrEmpty(app.DefaultArguments) ? null : app.DefaultArguments;

        if (!string.IsNullOrEmpty(launcherArguments))
        {
            var template = associationArgsTemplate ?? app.ArgumentsTemplate;
            if (!string.IsNullOrEmpty(template))
            {
                if (template.Contains("%1"))
                {
                    var sanitized = SanitizeForSubstitution(launcherArguments);
                    return template.Replace("%1", sanitized);
                }

                // Append mode: add space when template ends with alphanumeric or closing quote
                var last = template[^1];
                var sep = char.IsLetterOrDigit(last) || last == '"' ? " " : "";
                return template + sep + launcherArguments;
            }

            // No template: replace behavior (original)
            return launcherArguments;
        }

        return string.IsNullOrEmpty(app.DefaultArguments) ? null : app.DefaultArguments;
    }

    /// <summary>
    /// Escapes a value for safe substitution into a <c>%1</c> placeholder using MSVC CRT rules,
    /// so the escaped result is parsed as a single argument by <c>CommandLineToArgvW</c>.
    /// <para>
    /// Steps:
    /// 1. Strips outer quotes if the entire value is wrapped in matching double-quotes.
    /// 2. For each run of N backslashes immediately followed by <c>"</c>: emits 2N+1 backslashes + <c>"</c>.
    /// 3. Standalone <c>"</c> (no preceding backslashes counted in step 2): emits <c>\"</c>.
    /// 4. Backslashes not followed by <c>"</c> are emitted as-is.
    /// </para>
    /// </summary>
    public static string SanitizeForSubstitution(string value)
    {
        // Strip outer quotes (defensive — in case value arrives pre-quoted)
        if (value is ['"', _, ..] && value[^1] == '"')
            value = value.Substring(1, value.Length - 2);

        var sb = new StringBuilder(value.Length + 8);
        int i = 0;
        while (i < value.Length)
        {
            // Count run of backslashes
            int backslashStart = i;
            while (i < value.Length && value[i] == '\\')
                i++;
            int backslashCount = i - backslashStart;

            if (i < value.Length && value[i] == '"')
            {
                // N backslashes followed by " → 2N+1 backslashes + \"
                sb.Append('\\', backslashCount * 2 + 1);
                sb.Append('"');
                i++;
            }
            else
            {
                // Backslashes not followed by " → emit as-is
                sb.Append('\\', backslashCount);
                if (i < value.Length)
                    sb.Append(value[i++]);
            }
        }

        return sb.ToString();
    }

    public static string? DetermineWorkingDirectory(AppEntry app, string? launcherWorkingDirectory)
    {
        if (!app.AllowPassingWorkingDirectory)
            return string.IsNullOrWhiteSpace(app.WorkingDirectory) ? null : app.WorkingDirectory;

        if (!string.IsNullOrWhiteSpace(launcherWorkingDirectory))
            return launcherWorkingDirectory;

        return string.IsNullOrWhiteSpace(app.WorkingDirectory) ? null : app.WorkingDirectory;
    }

    public static bool ValidateUrlScheme(string url, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            error = "URL cannot be empty.";
            return false;
        }

        if (!PathHelper.IsUrlScheme(url))
        {
            error = "Invalid URL format. Expected scheme://... or scheme:...";
            return false;
        }

        var colonIndex = url.IndexOf(':');
        var scheme = url[..colonIndex].ToLowerInvariant();

        if (Constants.BlockedUrlSchemes.Contains(scheme))
        {
            error = $"URL scheme '{scheme}' is not allowed for security reasons.";
            return false;
        }

        if (HasUnsafeUrlLaunchCharacters(url))
        {
            error = "URL contains characters that are unsafe for command execution.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Escapes cmd.exe metacharacters by prefixing them with ^.
    /// The ^ escape is processed during cmd.exe's initial parsing pass and the
    /// resulting literal character is NOT re-evaluated as a metacharacter.
    /// </summary>
    public static string EscapeCmdMetacharacters(string value)
    {
        var sb = new StringBuilder(value.Length + 16);
        foreach (var c in value)
        {
            if (c is '&' or '|' or '^' or '<' or '>' or '!' or '(' or ')')
                sb.Append('^');
            sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool HasUnsafeUrlLaunchCharacters(string url)
    {
        foreach (var c in url)
        {
            // Double quotes break command-line argument boundaries for any process
            if (c == '"')
                return true;

            // Control characters (including \r, \n, \0) break command-line parsing
            if (c <= 0x1F || c == 0x7F)
                return true;
        }

        return false;
    }

    public static string BuildCommandLine(ProcessLaunchTarget psi)
    {
        var sb = new StringBuilder();
        AppendQuotedArg(sb, psi.ExePath);
        if (!string.IsNullOrEmpty(psi.Arguments))
        {
            sb.Append(' ');
            sb.Append(psi.Arguments);
        }

        return sb.ToString();
    }

    public static void AppendQuotedArg(StringBuilder sb, string arg)
    {
        if (arg.Length > 0 && !arg.Contains(' ') && !arg.Contains('"') && !arg.Contains('\t'))
        {
            sb.Append(arg);
            return;
        }

        // CommandLineToArgvW-compatible quoting: handle backslash sequences before quotes
        sb.Append('"');
        var backslashes = 0;
        foreach (var c in arg)
        {
            switch (c)
            {
                case '\\':
                    backslashes++;
                    break;
                case '"':
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    backslashes = 0;
                    break;
                default:
                {
                    if (backslashes > 0)
                    {
                        sb.Append('\\', backslashes);
                        backslashes = 0;
                    }

                    sb.Append(c);
                    break;
                }
            }
        }

        sb.Append('\\', backslashes * 2);
        sb.Append('"');
    }
}