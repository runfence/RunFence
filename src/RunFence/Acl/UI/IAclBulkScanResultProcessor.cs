namespace RunFence.Acl.UI;

public interface IAclBulkScanResultProcessor
{
    AclBulkScanImportSummary ApplyScanResults(
        Dictionary<string, AccountScanResult> selected,
        Action saveDatabase);
}
