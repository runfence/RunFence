namespace RunFence.Core.Helpers;

public static class SidComparer
{
    public static bool SidEquals(string? a, string? b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
