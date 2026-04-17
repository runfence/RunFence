namespace RunFence.Core.Helpers;

/// <summary>
/// Pure parsing helpers for extracting exe path and arguments from Windows registry command strings.
/// No IO, no side effects.
/// </summary>
public static class AssociationRegistryCommandParser
{
    /// <summary>
    /// Returns the first token (exe path) from <paramref name="commandLine"/>, unquoted.
    /// Returns null for null/empty input.
    /// </summary>
    public static string? ExtractExeFromCommand(string? commandLine)
    {
        if (string.IsNullOrEmpty(commandLine))
            return null;
        return CommandLineHelper.SplitArgs(commandLine).FirstOrDefault();
    }

    /// <summary>
    /// Returns true if <paramref name="args"/> represents no non-trivial arguments —
    /// null, empty, whitespace, <c>%1</c>, or <c>"%1"</c> (trimmed, OrdinalIgnoreCase).
    /// </summary>
    public static bool IsDefaultArgs(string? args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return true;
        var trimmed = args.Trim();
        return string.Equals(trimmed, "%1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "\"%1\"", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the arguments portion of <paramref name="commandLine"/> after the exe (first token),
    /// or null if the remainder is trivial (empty, <c>%1</c>, or <c>"%1"</c>).
    /// </summary>
    public static string? ExtractNonDefaultArgs(string? commandLine)
    {
        if (string.IsNullOrEmpty(commandLine))
            return null;
        var remainder = CommandLineHelper.SkipArgs(commandLine, 1)?.Trim();
        return IsDefaultArgs(remainder) ? null : remainder;
    }
}
