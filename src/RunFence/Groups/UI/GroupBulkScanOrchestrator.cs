using RunFence.Account;
using RunFence.Acl.UI;
using RunFence.Infrastructure;

namespace RunFence.Groups.UI;

public class GroupBulkScanOrchestrator(
    IModalCoordinator modalCoordinator,
    ILocalGroupMembershipService groupMembership,
    AclBulkScanWorkflow workflow,
    IAclBulkScanMessagePresenter messagePresenter)
{
    public async Task ScanAcls(
        IWin32Window owner,
        Action<bool> setScanButtonEnabled,
        Action<string> setStatusText,
        Action saveDatabase)
    {
        await workflow.RunAsync(new GroupAclBulkScanWorkflowContext(
            owner,
            setScanButtonEnabled,
            setStatusText,
            saveDatabase,
            modalCoordinator,
            groupMembership,
            messagePresenter));
    }
}
