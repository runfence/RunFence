namespace RunFence.SecurityScanner;

/// <summary>
/// Static helpers for parsing command-line strings into executable paths.
/// Handles plain paths, cmd.exe /c wrappers, and PowerShell -File/-Command invocations.
/// </summary>
public static class CommandLineParser
{
    private static readonly string[] s_executableExtensions =
        [".exe", ".cmd", ".bat", ".com", ".dll", ".ps1", ".scr", ".msi", ".reg", ".lnk"];

    private static readonly string[] s_psNoValueFlags =
        ["-NoLogo", "-NoExit", "-NonInteractive", "-NoProfile", "-MTA", "-STA", "-Help"];

    public static string? ExtractExecutablePath(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return null;

        var trimmed = SecurityScanner.ExpandEnvVars(commandLine.Trim());
        var firstToken = ParseFirstCommandToken(trimmed, out var remaining);
        if (firstToken == null)
            return null;

        var baseName = Path.GetFileNameWithoutExtension(firstToken);

        if (baseName.Equals("cmd", StringComparison.OrdinalIgnoreCase))
            return TryExtractCmdTarget(remaining) ?? firstToken;

        if (baseName.Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("pwsh", StringComparison.OrdinalIgnoreCase))
            return TryExtractPowerShellTarget(remaining) ?? firstToken;

        return firstToken;
    }

    // Extracts the first executable token from a command line (quoted or unquoted).
    // Handles unquoted paths with spaces by scanning for known executable extensions.
    private static string? ParseFirstCommandToken(string commandLine, out string remaining)
    {
        remaining = string.Empty;
        if (string.IsNullOrWhiteSpace(commandLine))
            return null;

        if (commandLine[0] == '"')
        {
            var end = commandLine.IndexOf('"', 1);
            if (end > 0)
            {
                var token = commandLine[1..end];
                remaining = commandLine[(end + 1)..].TrimStart();
                return string.IsNullOrWhiteSpace(token) ? null : token;
            }
        }

        var spaceIdx = commandLine.IndexOf(' ');
        if (spaceIdx < 0)
            return commandLine;

        var before = commandLine[..spaceIdx];
        if (!string.IsNullOrEmpty(Path.GetExtension(before)))
        {
            remaining = commandLine[(spaceIdx + 1)..].TrimStart();
            return before;
        }

        // Unquoted path with spaces: find known extension followed by space.
        // Constrain search to before the first '"' so quoted arguments don't match.
        // startsLikePath: only treat the whole string as an unquoted bare path (no args)
        // when it looks like an absolute path — prevents "cmd /c script.bat" matching ".bat".
        bool startsLikePath = commandLine is [_, ':', '\\', ..]
                              || commandLine.StartsWith("\\\\")
                              || commandLine.StartsWith('%');
        var firstQuoteIdx = commandLine.IndexOf('"');
        var extSearchEnd = firstQuoteIdx >= 0 ? firstQuoteIdx : commandLine.Length;
        foreach (var ext in s_executableExtensions)
        {
            var idx = commandLine.IndexOf(ext + " ", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && idx < extSearchEnd)
            {
                remaining = commandLine[(idx + ext.Length + 1)..].TrimStart();
                return commandLine[..(idx + ext.Length)];
            }

            if (startsLikePath && commandLine.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return commandLine;
        }

        remaining = commandLine[(spaceIdx + 1)..].TrimStart();
        return before;
    }

    // Parses the command after `cmd` to find the actual target executable.
    // Handles: cmd /c "start [/B] [path]", cmd /c "[path]", cmd /c path
    private static string? TryExtractCmdTarget(string remaining)
    {
        if (string.IsNullOrWhiteSpace(remaining))
            return null;
        var tokens = TokenizeCommandLine(remaining);
        int i = 0;

        // Skip switches until /c or /k (the payload separator), or a non-flag token
        while (i < tokens.Count && tokens[i].StartsWith('/'))
        {
            var flag = tokens[i].ToUpperInvariant();
            i++;
            if (flag is "/C" or "/K")
                break;
        }

        if (i >= tokens.Count)
            return null;

        var payload = tokens[i];
        if (string.IsNullOrEmpty(payload))
            return null;

        // Payload from a quoted block contains spaces — it's a full inner command
        if (payload.Contains(' '))
        {
            if (payload.StartsWith("start ", StringComparison.OrdinalIgnoreCase) ||
                payload.Equals("start", StringComparison.OrdinalIgnoreCase))
            {
                var inner = payload.Length > 6 ? payload[6..].TrimStart() : string.Empty;
                return ExtractStartTarget(TokenizeCommandLine(inner), 0);
            }

            // Inner command may be "path args" — extract just the executable
            return ParseFirstCommandToken(payload, out _);
        }

        // Unquoted: check for "start" keyword
        if (payload.Equals("start", StringComparison.OrdinalIgnoreCase))
            return i + 1 < tokens.Count ? ExtractStartTarget(tokens, i + 1) : null;

        return payload;
    }

    // Extracts the executable target from a `start` command's argument list.
    // Handles: start [/flags] ["optional title"] target
    private static string? ExtractStartTarget(List<string> tokens, int i)
    {
        bool titleSkipped = false;
        while (i < tokens.Count)
        {
            var token = tokens[i];
            if (string.IsNullOrEmpty(token))
            {
                i++;
                continue;
            }

            if (token[0] == '/')
            {
                i++;
                if (token.Equals("/D", StringComparison.OrdinalIgnoreCase) && i < tokens.Count)
                    i++;
                continue;
            }

            // A title has no path separators and no known executable extension
            if (!titleSkipped && !token.Contains('\\') && !token.Contains('/') &&
                !s_executableExtensions.Any(e => token.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            {
                if (i + 1 < tokens.Count)
                {
                    titleSkipped = true;
                    i++;
                    continue;
                }
            }

            return string.IsNullOrEmpty(token) ? null : token;
        }

        return null;
    }

    // Parses the arguments after `powershell` or `pwsh` to find the script path.
    // Handles: powershell [path], powershell -File path, powershell -Flags... -File path
    private static string? TryExtractPowerShellTarget(string remaining)
    {
        if (string.IsNullOrWhiteSpace(remaining))
            return null;
        var tokens = TokenizeCommandLine(remaining);

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (string.IsNullOrEmpty(token))
                continue;

            if (!token.StartsWith('-'))
                return token; // positional script path

            // -File (and abbreviations: -f, -fi, -fil, -file)
            if (token.Length >= 2 && "file".StartsWith(token[1..], StringComparison.OrdinalIgnoreCase))
                return ++i < tokens.Count ? tokens[i] : null;

            // -Command / -c: try to extract a path from the command string
            if (token.Equals("-Command", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("-c", StringComparison.OrdinalIgnoreCase))
                return ++i < tokens.Count ? TryExtractPathFromPsCommand(tokens[i]) : null;

            // Known boolean flags: no value to consume
            if (IsPowerShellBooleanFlag(token))
                continue;

            // All other flags consume the next token as their value
            i++;
        }

        return null;
    }

    // Tries to extract a file path from a PowerShell -Command value like "& 'path'"
    private static string? TryExtractPathFromPsCommand(string commandValue)
    {
        var cmd = commandValue.Trim();
        if (cmd.StartsWith('"') && cmd.EndsWith('"') && cmd.Length > 2)
            cmd = cmd[1..^1].Trim();
        if (cmd.StartsWith("& '", StringComparison.OrdinalIgnoreCase))
        {
            var end = cmd.IndexOf('\'', 3);
            if (end > 3)
                return cmd[3..end];
        }

        return null;
    }

    private static bool IsPowerShellBooleanFlag(string flag)
    {
        var flagName = flag.AsSpan(1);
        foreach (var known in s_psNoValueFlags)
            if (known.AsSpan(1).StartsWith(flagName, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    // Splits a command line into tokens, stripping surrounding quotes from each token.
    private static List<string> TokenizeCommandLine(string commandLine)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < commandLine.Length)
        {
            while (i < commandLine.Length && char.IsWhiteSpace(commandLine[i]))
                i++;
            if (i >= commandLine.Length)
                break;

            if (commandLine[i] == '"')
            {
                i++;
                var start = i;
                while (i < commandLine.Length && commandLine[i] != '"')
                    i++;
                tokens.Add(commandLine[start..i]);
                if (i < commandLine.Length)
                    i++;
            }
            else
            {
                var start = i;
                while (i < commandLine.Length && !char.IsWhiteSpace(commandLine[i]))
                    i++;
                tokens.Add(commandLine[start..i]);
            }
        }

        return tokens;
    }

    /// <summary>
    /// Resolves a non-rooted executable name (e.g. "cmd.exe") to its full path
    /// by searching the PATH environment variable, matching Windows behavior.
    /// Returns null if not found.
    /// </summary>
    public static string? ResolveViaPath(string fileName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
            return null;

        var hasExtension = Path.HasExtension(fileName);

        foreach (var dir in pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = dir.Trim();
            if (trimmed.Length == 0)
                continue;

            try
            {
                var candidate = Path.Combine(trimmed, fileName);
                if (File.Exists(candidate))
                    return candidate;

                if (!hasExtension)
                {
                    var withExe = candidate + ".exe";
                    if (File.Exists(withExe))
                        return withExe;
                }
            }
            catch
            {
                /* invalid path chars in PATH entry */
            }
        }

        return null;
    }

    public static List<string> ComputeUnquotedPathCandidates(string rawImagePath)
    {
        var candidates = new List<string>();
        if (string.IsNullOrWhiteSpace(rawImagePath))
            return candidates;

        var trimmed = rawImagePath.Trim();

        if (trimmed.StartsWith('"'))
            return candidates;

        trimmed = SecurityScanner.ExpandEnvVars(trimmed);

        var parts = trimmed.Split(' ');
        var accumulated = "";
        for (int i = 0; i < parts.Length - 1; i++)
        {
            accumulated = i == 0 ? parts[i] : accumulated + " " + parts[i];

            if (!accumulated.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var withExe = accumulated + ".exe";
                candidates.Add(withExe);
            }
        }

        return candidates;
    }
}