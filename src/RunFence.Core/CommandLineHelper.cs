using System.Text;

namespace RunFence.Core;

/// <summary>
/// Utilities for building and slicing Windows command-line argument strings.
/// </summary>
public static class CommandLineHelper
{
    /// <summary>
    /// Skips the first <paramref name="count"/> arguments in a raw Windows command-line string
    /// and returns the remainder verbatim (leading whitespace trimmed). Returns null when no
    /// arguments remain after skipping.
    /// <para>
    /// Use this to extract the "extra args" portion of <see cref="System.Environment.CommandLine"/>
    /// without any parse/re-quote round-trip that would lose the original quoting.
    /// For example, skipping 2 from <c>"launcher.exe" "appid" a b "c d" "e"</c>
    /// returns <c>a b "c d" "e"</c> verbatim.
    /// </para>
    /// </summary>
    public static string? SkipArgs(string cmdLine, int count)
    {
        int pos = 0;
        int len = cmdLine.Length;

        for (int i = 0; i < count; i++)
        {
            // Skip leading whitespace before this token
            while (pos < len && cmdLine[pos] == ' ')
                pos++;
            if (pos >= len)
                return null;

            // Advance past one argument using CommandLineToArgvW boundary rules
            bool inQuotes = false;
            while (pos < len)
            {
                char c = cmdLine[pos];
                if (c == '\\')
                {
                    // Count consecutive backslashes
                    int bsStart = pos;
                    while (pos < len && cmdLine[pos] == '\\')
                        pos++;
                    int bsCount = pos - bsStart;
                    if (pos < len && cmdLine[pos] == '"')
                    {
                        pos++; // consume the quote
                        if (bsCount % 2 == 0)
                        {
                            // Even backslashes: quote toggles in/out of quoted section
                            inQuotes = !inQuotes;
                        }
                        // Odd backslashes: literal quote — inQuotes state unchanged
                    }
                    // Backslashes not followed by quote are literal — no state change
                }
                else if (c == '"')
                {
                    inQuotes = !inQuotes;
                    pos++;
                }
                else if (c == ' ' && !inQuotes)
                {
                    break; // unquoted space ends this token
                }
                else
                {
                    pos++;
                }
            }
        }

        // Skip whitespace between the last skipped token and the remaining args
        while (pos < len && cmdLine[pos] == ' ')
            pos++;

        return pos < len ? cmdLine[pos..] : null;
    }

    /// <summary>
    /// Joins <paramref name="args"/> into a single command-line string using
    /// CommandLineToArgvW-compatible quoting. Returns null when the collection is
    /// null or empty.
    /// <para>
    /// Use this only when the source is a programmatic <c>List&lt;string&gt;</c> where the
    /// individual argument values are known (e.g. DragBridge, LaunchExe). Never use this to
    /// reconstruct a string from already-parsed shell arguments — use
    /// <see cref="SkipArgs"/> on <see cref="System.Environment.CommandLine"/> instead.
    /// </para>
    /// </summary>
    public static string? JoinArgs(IEnumerable<string>? args)
    {
        if (args == null)
            return null;

        var sb = new StringBuilder();
        foreach (var arg in args)
        {
            if (sb.Length > 0)
                sb.Append(' ');
            AppendQuotedArg(sb, arg);
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>
    /// Splits a Windows command-line string into an array of unquoted argument values,
    /// using CommandLineToArgvW boundary and escape rules: outer quotes are stripped;
    /// backslashes immediately before a <c>"</c> are halved (odd count → literal quote,
    /// even count → quote toggles in/out of quoted section); backslashes elsewhere are literal.
    /// </summary>
    public static string[] SplitArgs(string cmdLine)
    {
        var result = new List<string>();
        int pos = 0;
        int len = cmdLine.Length;

        while (pos < len)
        {
            while (pos < len && cmdLine[pos] == ' ')
                pos++;
            if (pos >= len)
                break;

            var token = new StringBuilder();
            bool inQuotes = false;

            while (pos < len)
            {
                char c = cmdLine[pos];
                if (c == '\\')
                {
                    int bsStart = pos;
                    while (pos < len && cmdLine[pos] == '\\')
                        pos++;
                    int bsCount = pos - bsStart;

                    if (pos < len && cmdLine[pos] == '"')
                    {
                        // Even backslashes: each pair → one literal backslash, quote toggles mode
                        // Odd backslashes: each pair → one literal backslash, last + quote → literal quote
                        token.Append('\\', bsCount / 2);
                        if (bsCount % 2 == 0)
                            inQuotes = !inQuotes;
                        else
                            token.Append('"');
                        pos++; // consume quote
                    }
                    else
                    {
                        // Backslashes not followed by quote are literal
                        token.Append('\\', bsCount);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = !inQuotes;
                    pos++;
                }
                else if (c == ' ' && !inQuotes)
                {
                    break;
                }
                else
                {
                    token.Append(c);
                    pos++;
                }
            }

            result.Add(token.ToString());
        }

        return result.ToArray();
    }

    // CommandLineToArgvW-compatible quoting: backslashes before a closing quote
    // must be doubled; a literal quote is preceded by a backslash.
    private static void AppendQuotedArg(StringBuilder sb, string arg)
    {
        if (arg.Length > 0 && !arg.Contains(' ') && !arg.Contains('"') && !arg.Contains('\t'))
        {
            sb.Append(arg);
            return;
        }

        sb.Append('"');
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
            }
            else if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
                backslashes = 0;
            }
            else
            {
                if (backslashes > 0)
                {
                    sb.Append('\\', backslashes);
                    backslashes = 0;
                }

                sb.Append(c);
            }
        }

        sb.Append('\\', backslashes * 2);
        sb.Append('"');
    }
}