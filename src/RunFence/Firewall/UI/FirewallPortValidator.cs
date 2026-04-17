namespace RunFence.Firewall.UI;

/// <summary>
/// Validates port entries for the localhost port allowlist.
/// Handles port/range parsing, duplicate detection, and limit checking.
/// </summary>
public class FirewallPortValidator
{
    /// <summary>
    /// Parses a port or range string to a <see cref="PortRange"/>. Returns null if invalid.
    /// Accepts single ports ("8080") and ranges ("8080-8090").
    /// </summary>
    public PortRange? ParsePortOrRange(string value) => LocalhostPortParser.ParsePortOrRange(value);

    /// <summary>
    /// Parses a <c>localhost:N</c> or <c>localhost:N-M</c> formatted string.
    /// Returns the parsed <see cref="PortRange"/>, or null if invalid.
    /// </summary>
    public PortRange? ParseLocalhostPort(string value)
    {
        const string prefix = "localhost:";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        return ParsePortOrRange(value[prefix.Length..]);
    }

    /// <summary>
    /// Returns true if an entry with the same value already exists in the list (case-insensitive).
    /// Optionally excludes one value (for in-place edit scenarios).
    /// </summary>
    public bool HasDuplicate(string value, IReadOnlyList<string> entries, string? excluding = null)
        => entries.Any(en => !string.Equals(en, excluding, StringComparison.OrdinalIgnoreCase)
                           && string.Equals(en, value, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns true if an additional port entry can be added (count is below the maximum).
    /// </summary>
    public bool CheckLimit(int count) => count < LocalhostPortParser.MaxAllowedPorts;
}
