namespace PrefTrans;

/// <summary>
/// Resolves command-line command names by unambiguous prefix matching.
/// Exact matches always win. If a prefix matches exactly one command, it resolves to that command.
/// If a prefix matches more than one command, a warning is written and null is returned.
/// </summary>
public static class CommandLineParser
{
    /// <summary>
    /// Resolves <paramref name="input"/> against <paramref name="knownCommands"/> by unambiguous prefix match.
    /// Returns the matched command name, or null if the input is ambiguous or unrecognized.
    /// Writes a warning to <see cref="Console.Error"/> when the prefix is ambiguous.
    /// </summary>
    public static string? ResolveCommand(string input, IReadOnlyList<string> knownCommands)
    {
        var exact = knownCommands.FirstOrDefault(c =>
            string.Equals(c, input, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact;

        var matches = knownCommands
            .Where(c => c.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 1)
            return matches[0];

        if (matches.Count > 1)
            Console.Error.WriteLine(
                $"Warning: '{input}' is ambiguous — matches: {string.Join(", ", matches)}");

        return null;
    }
}
