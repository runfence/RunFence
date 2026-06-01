namespace RunFence.Launch;

public readonly record struct VersionedPathRepairResult(
    string RepairedPath,
    string OriginalAncestorPath,
    string CandidateAncestorPath,
    string OriginalAncestorName,
    string CandidateAncestorName);
