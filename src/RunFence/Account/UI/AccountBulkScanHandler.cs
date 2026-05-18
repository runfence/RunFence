using RunFence.Acl.UI;

namespace RunFence.Account.UI;

public class AccountBulkScanHandler(
    AclBulkScanWorkflow workflow,
    IAclBulkScanMessagePresenter messagePresenter) : IAccountBulkScanHandler
{
    public async Task ScanAcls(
        IAccountsPanelContext context,
        IScanProgressReporter progressReporter)
    {
        await workflow.RunAsync(new AccountAclBulkScanWorkflowContext(context, progressReporter, messagePresenter));
    }
}
