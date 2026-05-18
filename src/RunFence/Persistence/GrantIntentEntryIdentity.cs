using RunFence.Core.Models;

namespace RunFence.Persistence;

internal readonly record struct GrantIntentEntryIdentity(
    string OwnerSid,
    string Path,
    bool IsDeny,
    bool IsTraverseOnly,
    SavedRightsState? SavedRights,
    IReadOnlyList<string> AllAppliedPaths,
    IReadOnlyList<string> SourceSids)
{
    public static GrantIntentEntryIdentity From(string ownerSid, GrantedPathEntry entry)
        => new(
            NormalizeSid(ownerSid),
            NormalizePath(entry.Path),
            entry.IsDeny,
            entry.IsTraverseOnly,
            entry.SavedRights,
            NormalizePaths(entry.AllAppliedPaths),
            NormalizeSids(entry.SourceSids));

    public static GrantIntentEntryIdentity From(
        string ownerSid,
        string path,
        bool isDeny,
        bool isTraverseOnly,
        SavedRightsState? savedRights,
        IReadOnlyList<string>? allAppliedPaths,
        IReadOnlyList<string>? sourceSids)
        => new(
            NormalizeSid(ownerSid),
            NormalizePath(path),
            isDeny,
            isTraverseOnly,
            savedRights,
            NormalizePaths(allAppliedPaths),
            NormalizeSids(sourceSids));

    public bool Equals(GrantIntentEntryIdentity other)
        => string.Equals(OwnerSid, other.OwnerSid, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase) &&
           IsDeny == other.IsDeny &&
           IsTraverseOnly == other.IsTraverseOnly &&
           Equals(SavedRights, other.SavedRights) &&
           SequenceEqual(AllAppliedPaths, other.AllAppliedPaths) &&
           SequenceEqual(SourceSids, other.SourceSids);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(OwnerSid, StringComparer.OrdinalIgnoreCase);
        hash.Add(Path, StringComparer.OrdinalIgnoreCase);
        hash.Add(IsDeny);
        hash.Add(IsTraverseOnly);
        hash.Add(SavedRights);
        AddSequenceHash(hash, AllAppliedPaths);
        AddSequenceHash(hash, SourceSids);
        return hash.ToHashCode();
    }

    private static string NormalizeSid(string sid)
        => sid.Trim();

    private static string NormalizePath(string path)
        => System.IO.Path.GetFullPath(path);

    private static IReadOnlyList<string> NormalizePaths(IReadOnlyList<string>? paths)
        => NormalizeSequence(paths, NormalizePath);

    private static IReadOnlyList<string> NormalizeSids(IReadOnlyList<string>? sids)
        => NormalizeSequence(sids, NormalizeSid);

    private static IReadOnlyList<string> NormalizeSequence(
        IReadOnlyList<string>? values,
        Func<string, string> normalize)
    {
        if (values == null || values.Count == 0)
            return [];

        return values
            .Select(normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool SequenceEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
            return false;

        for (int i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static void AddSequenceHash(HashCode hash, IReadOnlyList<string> values)
    {
        hash.Add(values.Count);
        foreach (var value in values)
            hash.Add(value, StringComparer.OrdinalIgnoreCase);
    }
}
