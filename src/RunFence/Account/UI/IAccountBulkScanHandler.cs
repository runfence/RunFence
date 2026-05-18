namespace RunFence.Account.UI;

public interface IAccountBulkScanHandler
{
    Task ScanAcls(IAccountsPanelContext context, IScanProgressReporter progressReporter);
}
