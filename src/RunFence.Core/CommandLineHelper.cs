using System.Text;

namespace RunFence.Core;

/// <summary>
/// Utilities for Windows process command-line parsing/materialization and verbatim-tail slicing.
/// </summary>
public static class CommandLineHelper
{
    public static string[] ParseProcessCommandLine(string commandLine)
    {
        var result = new List<string>();
        var pos = 0;

        if (TryReadProgramNameArgument(commandLine, ref pos, out var programName))
            result.Add(programName);

        while (TryReadNextArgument(commandLine, ref pos, out var argument))
            result.Add(argument);

        return result.ToArray();
    }

    public static string[] ParseProcessArguments(string commandLine)
    {
        var result = new List<string>();
        var pos = 0;

        while (TryReadNextArgument(commandLine, ref pos, out var argument))
            result.Add(argument);

        return result.ToArray();
    }

    public static string QuoteProcessArgument(string argument)
    {
        var sb = new StringBuilder(argument.Length + 8);
        AppendQuotedProcessArgument(sb, argument);
        return sb.ToString();
    }

    public static string? MaterializeProcessArguments(IEnumerable<string>? arguments)
    {
        if (arguments == null)
            return null;

        var sb = new StringBuilder();
        foreach (var argument in arguments)
        {
            if (sb.Length > 0)
                sb.Append(' ');
            AppendQuotedProcessArgument(sb, argument);
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    public static string? SliceVerbatimTail(string commandLine, int count)
    {
        var pos = 0;
        for (var i = 0; i < count; i++)
        {
            if (!TrySkipNextArgument(commandLine, ref pos))
                return null;
        }

        SkipWhitespace(commandLine, ref pos);
        return pos < commandLine.Length ? commandLine[pos..] : null;
    }

    // Compatibility wrappers for existing callers.
    public static string[] SplitArgs(string cmdLine) => ParseProcessArguments(cmdLine);

    public static string? JoinArgs(IEnumerable<string>? args) => MaterializeProcessArguments(args);

    public static string? SkipArgs(string cmdLine, int count) => SliceVerbatimTail(cmdLine, count);

    private static void AppendQuotedProcessArgument(StringBuilder sb, string argument)
    {
        if (argument.Length > 0 && !argument.Contains(' ') && !argument.Contains('\t') && !argument.Contains('"'))
        {
            sb.Append(argument);
            return;
        }

        sb.Append('"');
        var backslashes = 0;
        foreach (var c in argument)
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
                    if (backslashes > 0)
                    {
                        sb.Append('\\', backslashes);
                        backslashes = 0;
                    }

                    sb.Append(c);
                    break;
            }
        }

        sb.Append('\\', backslashes * 2);
        sb.Append('"');
    }

    private static bool TryReadNextArgument(string commandLine, ref int pos, out string argument)
    {
        argument = string.Empty;
        SkipWhitespace(commandLine, ref pos);
        if (pos >= commandLine.Length)
            return false;

        var token = new StringBuilder();
        var inQuotes = false;

        while (pos < commandLine.Length)
        {
            var c = commandLine[pos];
            if (c == '\\')
            {
                var start = pos;
                while (pos < commandLine.Length && commandLine[pos] == '\\')
                    pos++;
                var count = pos - start;

                if (pos < commandLine.Length && commandLine[pos] == '"')
                {
                    token.Append('\\', count / 2);
                    if (count % 2 == 0)
                        inQuotes = !inQuotes;
                    else
                        token.Append('"');
                    pos++;
                    continue;
                }

                token.Append('\\', count);
                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                pos++;
                continue;
            }

            if (!inQuotes && IsArgumentWhitespace(c))
                break;

            token.Append(c);
            pos++;
        }

        argument = token.ToString();
        return true;
    }

    private static bool TryReadProgramNameArgument(string commandLine, ref int pos, out string argument)
    {
        argument = string.Empty;
        SkipWhitespace(commandLine, ref pos);
        if (pos >= commandLine.Length)
            return false;

        var token = new StringBuilder();
        var inQuotes = false;
        while (pos < commandLine.Length)
        {
            var c = commandLine[pos];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                pos++;
                continue;
            }

            if (!inQuotes && IsArgumentWhitespace(c))
                break;

            token.Append(c);
            pos++;
        }

        argument = token.ToString();
        return true;
    }

    private static bool TrySkipNextArgument(string commandLine, ref int pos)
    {
        SkipWhitespace(commandLine, ref pos);
        if (pos >= commandLine.Length)
            return false;

        var inQuotes = false;
        while (pos < commandLine.Length)
        {
            var c = commandLine[pos];
            if (c == '\\')
            {
                var start = pos;
                while (pos < commandLine.Length && commandLine[pos] == '\\')
                    pos++;
                var count = pos - start;

                if (pos < commandLine.Length && commandLine[pos] == '"')
                {
                    pos++;
                    if (count % 2 == 0)
                        inQuotes = !inQuotes;
                }

                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                pos++;
                continue;
            }

            if (!inQuotes && IsArgumentWhitespace(c))
                break;

            pos++;
        }

        return true;
    }

    private static void SkipWhitespace(string commandLine, ref int pos)
    {
        while (pos < commandLine.Length && IsArgumentWhitespace(commandLine[pos]))
            pos++;
    }

    private static bool IsArgumentWhitespace(char c) => c is ' ' or '\t';
}
