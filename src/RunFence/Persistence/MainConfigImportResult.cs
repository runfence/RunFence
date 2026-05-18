namespace RunFence.Persistence;

public sealed record MainConfigImportResult(
    IReadOnlyList<string> Warnings,
    string? SaveError);
