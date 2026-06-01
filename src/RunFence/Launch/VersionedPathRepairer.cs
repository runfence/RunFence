using RunFence.Infrastructure;
using RunFence.Launching.Resolution;
using RunFence.Core;

namespace RunFence.Launch;

public sealed class VersionedPathRepairer(IBackupIntentFileSystem fileSystem)
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparer NameComparer = StringComparer.Ordinal;
    private static readonly HashSet<string> HardBoundaryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Program Files",
        "Program Files (x86)",
        "WindowsApps",
        "Users"
    };

    public VersionedPathRepairResult? TryRepair(string path, bool isFolder, VersionedPathRepairOptions options)
    {
        if (!RootedLocalPathNormalizer.TryNormalizeRootedLocalPath(path, out var normalizedPath))
            return null;

        var originalState = isFolder
            ? fileSystem.GetDirectoryState(normalizedPath)
            : fileSystem.GetFileState(normalizedPath);
        if (originalState != BackupIntentPathState.Missing)
            return null;

        var boundaries = NormalizeBoundaryPaths(options.UnversionedBoundaryPaths);
        var currentAncestorPath = isFolder
            ? TrimEndingSeparators(normalizedPath)
            : Path.GetDirectoryName(normalizedPath);
        while (!string.IsNullOrEmpty(currentAncestorPath))
        {
            var attempt = TryRepairAncestor(normalizedPath, isFolder, currentAncestorPath, boundaries, out var result);
            if (attempt == RepairAncestorStatus.Success)
                return result;

            if (attempt == RepairAncestorStatus.Abort)
                return null;

            currentAncestorPath = Path.GetDirectoryName(TrimEndingSeparators(currentAncestorPath));
        }

        return null;
    }

    private RepairAncestorStatus TryRepairAncestor(
        string normalizedTargetPath,
        bool isFolder,
        string ancestorPath,
        HashSet<string> boundaries,
        out VersionedPathRepairResult? result)
    {
        result = null;

        if (IsUnversionedBoundary(ancestorPath, boundaries))
            return RepairAncestorStatus.Continue;

        var ancestorName = Path.GetFileName(TrimEndingSeparators(ancestorPath));
        if (!VersionedDirectoryNameParser.TryParse(ancestorName, out var originalDirectory))
            return RepairAncestorStatus.Continue;

        var parentPath = Path.GetDirectoryName(ancestorPath);
        if (string.IsNullOrEmpty(parentPath))
        {
            return RepairAncestorStatus.Continue;
        }

        var parentState = fileSystem.GetDirectoryState(parentPath);
        if (parentState == BackupIntentPathState.Unknown)
            return RepairAncestorStatus.Abort;

        if (parentState != BackupIntentPathState.Exists)
            return RepairAncestorStatus.Continue;

        if (!fileSystem.TryEnumerateDirectories(parentPath, out var siblingDirectories))
            return RepairAncestorStatus.Abort;

        CandidateDirectory? bestCandidate = null;
        var relativeTail = Path.GetRelativePath(ancestorPath, normalizedTargetPath);

        foreach (var siblingDirectory in siblingDirectories)
        {
            var siblingName = Path.GetFileName(TrimEndingSeparators(siblingDirectory));
            if (!VersionedDirectoryNameParser.TryParse(siblingName, out var siblingDirectoryName))
                continue;

            if (!string.Equals(originalDirectory.Prefix, siblingDirectoryName.Prefix, StringComparison.OrdinalIgnoreCase)
                || !SuffixIdentityMatches(originalDirectory.Suffix, siblingDirectoryName.Suffix))
            {
                continue;
            }

            var candidateTargetPath = BuildCandidateTargetPath(siblingDirectory, relativeTail);
            var candidateState = isFolder
                ? fileSystem.GetDirectoryState(candidateTargetPath)
                : fileSystem.GetFileState(candidateTargetPath);
            if (candidateState != BackupIntentPathState.Exists)
                continue;

            var candidate = new CandidateDirectory(
                siblingDirectory,
                siblingDirectoryName,
                fileSystem.TryGetDirectoryLastWriteTimeUtc(siblingDirectory, out var lastWriteTimeUtc)
                    ? lastWriteTimeUtc
                    : null);
            if (bestCandidate == null || CompareCandidates(candidate, bestCandidate.Value) > 0)
                bestCandidate = candidate;
        }

        if (bestCandidate == null)
            return RepairAncestorStatus.Continue;

        var selected = bestCandidate.Value;
        result = new VersionedPathRepairResult(
            BuildCandidateTargetPath(selected.Path, relativeTail),
            ancestorPath,
            selected.Path,
            originalDirectory.OriginalName,
            selected.DirectoryName.OriginalName);
        return RepairAncestorStatus.Success;
    }

    private static string BuildCandidateTargetPath(string candidateAncestorPath, string relativeTail)
    {
        return relativeTail == "."
            ? candidateAncestorPath
            : Path.Combine(candidateAncestorPath, relativeTail);
    }

    private static int CompareCandidates(CandidateDirectory left, CandidateDirectory right)
    {
        var versionComparison = left.DirectoryName.SemanticVersionKey.CompareTo(right.DirectoryName.SemanticVersionKey);
        if (versionComparison != 0)
            return versionComparison;

        if (left.LastWriteTimeUtc.HasValue && right.LastWriteTimeUtc.HasValue)
        {
            var writeComparison = left.LastWriteTimeUtc.Value.CompareTo(right.LastWriteTimeUtc.Value);
            if (writeComparison != 0)
                return writeComparison;
        }

        return NameComparer.Compare(left.DirectoryName.OriginalName, right.DirectoryName.OriginalName);
    }

    private static bool SuffixIdentityMatches(string originalSuffix, string candidateSuffix)
    {
        var originalIdentity = StableSuffixIdentity.Parse(originalSuffix);
        var candidateIdentity = StableSuffixIdentity.Parse(candidateSuffix);

        if (originalIdentity.Architecture != null || candidateIdentity.Architecture != null)
        {
            if (!string.Equals(originalIdentity.Architecture, candidateIdentity.Architecture, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (originalIdentity.PublisherIdentity != null || candidateIdentity.PublisherIdentity != null)
        {
            if (!string.Equals(originalIdentity.PublisherIdentity, candidateIdentity.PublisherIdentity, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static HashSet<string> NormalizeBoundaryPaths(IReadOnlyList<string> boundaryPaths)
    {
        var normalized = new HashSet<string>(PathComparer);
        foreach (var boundaryPath in boundaryPaths)
        {
            if (!RootedLocalPathNormalizer.TryNormalizeRootedLocalBoundaryPath(boundaryPath, out var normalizedBoundary))
                continue;

            normalized.Add(normalizedBoundary);
        }

        return normalized;
    }

    private static bool IsUnversionedBoundary(string path, HashSet<string> boundaries)
    {
        var trimmedPath = TrimEndingSeparators(path);
        if (boundaries.Contains(trimmedPath))
            return true;

        var root = Path.GetPathRoot(trimmedPath);
        if (!string.IsNullOrEmpty(root)
            && string.Equals(TrimEndingSeparators(root), trimmedPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = Path.GetFileName(trimmedPath);
        if (HardBoundaryNames.Contains(name))
            return true;

        var parentName = Path.GetFileName(Path.GetDirectoryName(trimmedPath) ?? string.Empty);
        return string.Equals(parentName, "Users", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimEndingSeparators(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private readonly record struct CandidateDirectory(
        string Path,
        VersionedDirectoryNameParser.VersionedDirectoryName DirectoryName,
        DateTime? LastWriteTimeUtc);

    private enum RepairAncestorStatus
    {
        Continue,
        Success,
        Abort
    }

    private readonly record struct StableSuffixIdentity(string? Architecture, string? PublisherIdentity)
    {
        public static StableSuffixIdentity Parse(string suffix)
        {
            if (string.IsNullOrEmpty(suffix))
                return default;

            var architecture = ExtractArchitecture(suffix);
            var publisherIdentity = ExtractPublisherIdentity(suffix);
            return new StableSuffixIdentity(architecture, publisherIdentity);
        }

        private static string? ExtractArchitecture(string suffix)
        {
            foreach (var token in suffix.Split(['_', '-', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token.Equals("x64", StringComparison.OrdinalIgnoreCase)
                    || token.Equals("x86", StringComparison.OrdinalIgnoreCase)
                    || token.Equals("arm64", StringComparison.OrdinalIgnoreCase))
                {
                    return token;
                }
            }

            return null;
        }

        private static string? ExtractPublisherIdentity(string suffix)
        {
            var separatorIndex = suffix.LastIndexOf("__", StringComparison.Ordinal);
            if (separatorIndex < 0 || separatorIndex + 2 >= suffix.Length)
                return null;

            return suffix[(separatorIndex + 2)..];
        }
    }
}
