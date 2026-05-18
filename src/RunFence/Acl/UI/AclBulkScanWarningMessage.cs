namespace RunFence.Acl.UI;

public static class AclBulkScanWarningMessage
{
    public static string? BuildSkippedConflictWarningMessage(AclBulkScanImportSummary summary)
        => summary.SkippedOppositeModeConflictPaths.Count == 0
            ? null
            : "Some scanned ACL entries were skipped because the account already has the opposite grant mode for that path.\n\n" +
              string.Join("\n", summary.SkippedOppositeModeConflictPaths);
}
