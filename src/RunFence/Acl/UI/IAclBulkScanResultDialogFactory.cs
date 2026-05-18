using RunFence.Account;

namespace RunFence.Acl.UI;

public interface IAclBulkScanResultDialogFactory
{
    IAclBulkScanResultDialog Create(
        Dictionary<string, AccountScanResult> results,
        ISidNameCacheService sidNameCache);
}
