namespace RunFence.Firewall;

/// <summary>
/// Represents a single port (Low == High) or an inclusive port range.
/// </summary>
public readonly record struct PortRange(int Low, int High)
{
    public bool IsSingle => Low == High;
    public override string ToString() => IsSingle ? Low.ToString() : $"{Low}-{High}";
}

public static class LocalhostPortParser
{
    public const int MaxAllowedPorts = 16;

    /// <summary>
    /// Parses a port string ("53") or range string ("8080-8090") to a <see cref="PortRange"/>.
    /// Returns null for invalid input: non-numeric, out of range (1–65535), low &gt; high.
    /// </summary>
    public static PortRange? ParsePortOrRange(string value)
    {
        var trimmed = value.Trim();
        var dashIndex = trimmed.IndexOf('-');
        if (dashIndex < 0)
        {
            if (!int.TryParse(trimmed, out var port) || port < 1 || port > 65535)
                return null;
            return new PortRange(port, port);
        }

        if (dashIndex == 0 || dashIndex == trimmed.Length - 1)
            return null;
        if (!int.TryParse(trimmed[..dashIndex].Trim(), out var low) ||
            !int.TryParse(trimmed[(dashIndex + 1)..].Trim(), out var high))
            return null;
        if (low < 1 || high > 65535 || low > high)
            return null;
        return new PortRange(low, high);
    }

    /// <summary>
    /// Parses a port string to a valid single port number (1–65535).
    /// Returns null for invalid input: non-numeric, out of range, ranges.
    /// </summary>
    public static int? ParsePort(string value)
    {
        if (!int.TryParse(value.Trim(), out var port) || port < 1 || port > 65535)
            return null;
        return port;
    }

    /// <summary>
    /// Converts a list of individual port numbers into a sorted, merged list of
    /// <see cref="PortRange"/> values. Adjacent port numbers (distance ≤ 1) are merged
    /// into a single range. Used to coalesce dynamically-discovered ephemeral ports
    /// before writing them as WFP MATCH_RANGE conditions.
    /// </summary>
    public static List<PortRange> CoalescePortRanges(IEnumerable<int> ports)
    {
        var sorted = ports.OrderBy(p => p).ToList();
        var result = new List<PortRange>();
        foreach (var p in sorted)
        {
            if (result.Count > 0 && p <= result[^1].High + 1)
                result[^1] = new PortRange(result[^1].Low, Math.Max(result[^1].High, p));
            else
                result.Add(new PortRange(p, p));
        }
        return result;
    }
}
