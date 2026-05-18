namespace RunFence.Acl.UI;

public sealed record AclBulkScanImportSummary(
    int ImportedCount,
    int UpdatedCount,
    IReadOnlyList<string> SkippedOppositeModeConflictPaths)
{
    public bool HasChanges => ImportedCount > 0 || UpdatedCount > 0;
}
