using System.Text.RegularExpressions;

namespace RunFence.Launching.Resolution;

public static class VersionedDirectoryNameParser
{
    private static readonly Regex VersionRegex = new(
        @"(?<!\d)(?<version>\d+(?:\.\d+)+(?:[A-Za-z][A-Za-z0-9]*)?)(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FinalComponentRegex = new(
        @"^(?<number>\d+)(?<suffix>[A-Za-z][A-Za-z0-9]*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParse(string folderName, out VersionedDirectoryName directoryName)
    {
        directoryName = default;

        if (string.IsNullOrWhiteSpace(folderName))
            return false;

        var matches = VersionRegex.Matches(folderName);
        if (matches.Count != 1)
            return false;

        var versionMatch = matches[0].Groups["version"];
        if (!TryCreateSemanticVersionKey(versionMatch.Value, out var semanticVersionKey))
            return false;

        directoryName = new VersionedDirectoryName(
            folderName[..versionMatch.Index],
            versionMatch.Value,
            folderName[(versionMatch.Index + versionMatch.Length)..],
            folderName,
            semanticVersionKey);
        return true;
    }

    private static bool TryCreateSemanticVersionKey(string versionToken, out SemanticVersionKey semanticVersionKey)
    {
        semanticVersionKey = default;

        var parts = versionToken.Split('.');
        if (parts.Length < 2)
            return false;

        var numericParts = new int[parts.Length];
        string? suffix = null;

        for (var i = 0; i < parts.Length; i++)
        {
            var match = FinalComponentRegex.Match(parts[i]);
            if (!match.Success)
                return false;

            if (!int.TryParse(match.Groups["number"].Value, out numericParts[i]))
                return false;

            if (i != parts.Length - 1 && match.Groups["suffix"].Success)
                return false;

            if (i == parts.Length - 1 && match.Groups["suffix"].Success)
                suffix = match.Groups["suffix"].Value;
        }

        semanticVersionKey = new SemanticVersionKey(numericParts, suffix, versionToken);
        return true;
    }

    public readonly record struct VersionedDirectoryName(
        string Prefix,
        string Version,
        string Suffix,
        string OriginalName,
        SemanticVersionKey SemanticVersionKey);

    public readonly record struct SemanticVersionKey(
        IReadOnlyList<int> NumericParts,
        string? Suffix,
        string OriginalToken) : IComparable<SemanticVersionKey>
    {
        public int CompareTo(SemanticVersionKey other)
        {
            var maxLength = Math.Max(NumericParts.Count, other.NumericParts.Count);
            for (var i = 0; i < maxLength; i++)
            {
                var left = i < NumericParts.Count ? NumericParts[i] : 0;
                var right = i < other.NumericParts.Count ? other.NumericParts[i] : 0;
                var numericComparison = left.CompareTo(right);
                if (numericComparison != 0)
                    return numericComparison;
            }

            if (Suffix == null && other.Suffix != null)
                return 1;

            if (Suffix != null && other.Suffix == null)
                return -1;

            return StringComparer.OrdinalIgnoreCase.Compare(Suffix, other.Suffix);
        }
    }
}
