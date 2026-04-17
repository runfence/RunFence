using RunFence.Acl;
using RunFence.Core.Models;

namespace RunFence.Account.UI;

public interface IAccountBulkScanHandler
{
    Dictionary<string, AccountScanResult> FilterManagedPaths(
        Dictionary<string, AccountScanResult> results,
        IReadOnlyList<AppEntry> apps,
        IAclService aclService);

    void ApplyScanResults(
        Dictionary<string, AccountScanResult> selected,
        Action saveDatabase);
}
