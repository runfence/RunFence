namespace RunFence.Firewall;

public static class FirewallStringHelper
{
    /// <summary>
    /// Returns distinct non-whitespace strings using case-insensitive ordinal comparison.
    /// Skips null, empty, or whitespace-only entries when <paramref name="skipWhitespace"/> is true (default).
    /// </summary>
    public static IEnumerable<string> DistinctCaseInsensitive(
        this IEnumerable<string> values,
        bool skipWhitespace = true)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (skipWhitespace && string.IsNullOrWhiteSpace(value))
                continue;

            if (seen.Add(value))
                yield return value;
        }
    }
}
