namespace RunFence.Acl.UI;

public interface IAclBulkScanWarningPresenter
{
    void ShowSkippedConflictWarning(AclBulkScanImportSummary summary, string title);
}
