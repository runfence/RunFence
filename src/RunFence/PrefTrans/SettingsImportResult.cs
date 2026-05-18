namespace RunFence.PrefTrans;

public record SettingsImportResult(
    SettingsImportStatus Status,
    int ImportedAccounts,
    int ImportedApps,
    IReadOnlyList<string> SkippedItems,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> Errors,
    bool DatabaseModified);
