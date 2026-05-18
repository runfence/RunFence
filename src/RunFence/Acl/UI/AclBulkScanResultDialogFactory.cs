using RunFence.Account;
using RunFence.Acl.UI.Forms;

namespace RunFence.Acl.UI;

public sealed class AclBulkScanResultDialogFactory : IAclBulkScanResultDialogFactory
{
    public IAclBulkScanResultDialog Create(
        Dictionary<string, AccountScanResult> results,
        ISidNameCacheService sidNameCache)
        => new AclBulkScanResultDialog(results, sidNameCache);
}
