namespace RunFence.Core;

public static class CaseInsensitiveTupleComparer
{
    public static IEqualityComparer<(string, string)> Instance { get; } =
        EqualityComparer<(string, string)>.Create(
            (a, b) => string.Equals(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase)
                      && string.Equals(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase),
            x => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(x.Item1),
                StringComparer.OrdinalIgnoreCase.GetHashCode(x.Item2)));
}