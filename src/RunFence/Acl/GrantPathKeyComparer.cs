namespace RunFence.Acl;

/// <summary>
/// Case-insensitive comparer for (Path, IsDeny) grant path keys.
/// Tuple element names are erased at runtime, so this also serves as
/// an <see cref="IEqualityComparer{T}"/> for any <c>(string, bool)</c> variant.
/// </summary>
public sealed class GrantPathKeyComparer : IEqualityComparer<(string Path, bool IsDeny)>
{
    public bool Equals((string Path, bool IsDeny) x, (string Path, bool IsDeny) y) =>
        string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase) && x.IsDeny == y.IsDeny;

    public int GetHashCode((string Path, bool IsDeny) obj) =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Path), obj.IsDeny);
}
