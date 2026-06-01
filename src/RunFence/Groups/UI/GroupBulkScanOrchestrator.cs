using RunFence.Account;
using RunFence.Acl.UI;
using RunFence.Infrastructure;

namespace RunFence.Groups.UI;

public class GroupBulkScanOrchestrator(
    IModalCoordinator modalCoordinator,
    ILocalGroupQueryService groupMembership,
    AclBulkScanWorkflow workflow,
    IAclBulkScanMessagePresenter messagePresenter,
    ISessionSaver sessionSaver)
{
    public async Task ScanAcls(
        IWin32Window owner,
        IGroupScanProgressPresenter progressPresenter)
    {
        await workflow.RunAsync(new GroupAclBulkScanWorkflowContext(
            owner,
            progressPresenter,
            sessionSaver,
            modalCoordinator,
            groupMembership,
            messagePresenter));
    }
}
