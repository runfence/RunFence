using RunFence.Core.Models;

namespace RunFence.Acl.UI;

public interface IAclBulkScanResultProcessor
{
    Dictionary<string, AccountScanResult> FilterManagedPaths(
        Dictionary<string, AccountScanResult> results,
        IReadOnlyList<AppEntry> apps,
        IAclService aclService);

    AclBulkScanImportSummary ApplyScanResults(
        Dictionary<string, AccountScanResult> selected,
        Action saveDatabase);
}
