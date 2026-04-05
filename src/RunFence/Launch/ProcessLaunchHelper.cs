using System.Text;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch.Container;

namespace RunFence.Launch;

/// <summary>
/// Static helpers for process launch argument/working-directory resolution, URL validation,
/// and cmd.exe metacharacter escaping. Shared between <see cref="ProcessLaunchService"/>
/// and <see cref="AppContainerService"/>.
/// </summary>
public static class ProcessLaunchHelper
{
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
    public static string? DetermineArguments(AppEntry app, string? launcherArguments)
    {
        if (!app.AllowPassingArguments)
            return string.IsNullOrEmpty(app.DefaultArguments) ? null : app.DefaultArguments;

        if (!string.IsNullOrEmpty(launcherArguments))
        {
            var template = app.ArgumentsTemplate;
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

        if (Constants.BlockedUrlSchemes.Any(blocked => scheme == blocked))
        {
            error = $"URL scheme '{scheme}' is not allowed for security reasons.";
            return false;
        }

        if (HasUnescapableCmdCharacters(url))
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

    private static bool HasUnescapableCmdCharacters(string url)
    {
        foreach (var c in url)
        {
            switch (c)
            {
                // Double quotes cannot be safely escaped for cmd.exe /c
                case '"':
                // Percent signs trigger environment variable expansion; ^% does not
                // prevent expansion in cmd.exe /c, so there is no safe escape
                case '%':
                // Spaces cannot be reliably escaped for the 'start' built-in:
                // ^<space> prevents cmd.exe command separation but start still
                // splits on spaces when parsing its own arguments
                case ' ':
                    return true;
            }

            // Control characters (including \r, \n, \0) can break command parsing
            if (c <= 0x1F || c == 0x7F)
                return true;
        }

        return false;
    }
}