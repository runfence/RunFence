namespace RunFence.Account.UI;

public interface IAccountBulkScanHandler : IAccountScanResultProcessor
{
    Task ScanAcls(IAccountsPanelContext context, IScanProgressReporter progressReporter);
}
