using RunFence.Account;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

public sealed class AclBulkScanWorkflow(
    IAccountAclBulkScanService bulkScan,
    IAclService aclService,
    ILoggingService log,
    ISidNameCacheService sidNameCache,
    IAclBulkScanResultProcessor resultProcessor,
    IAclBulkScanWarningPresenter warningPresenter,
    IAclBulkScanResultDialogFactory bulkScanResultDialogFactory,
    IFolderBrowserDialogAdapterFactory folderBrowserDialogFactory,
    IDatabaseProvider databaseProvider)
{
    public async Task RunAsync(IAclBulkScanWorkflowContext context)
    {
        using var folderDialog = folderBrowserDialogFactory.Create();
        folderDialog.Dialog.Description = "Select a root folder to scan for ACLs";
        folderDialog.Dialog.UseDescriptionForTitle = true;
        if (folderDialog.ShowDialog(context.Owner) != DialogResult.OK)
            return;

        var rootPath = folderDialog.Dialog.SelectedPath;
        if (string.IsNullOrEmpty(rootPath))
            return;

        var knownSids = await context.GetKnownSidsAsync();
        if (knownSids.Count == 0)
        {
            context.ShowNoKnownSids();
            return;
        }

        context.SetStatusText("Scanning ACLs...");
        context.SetScanBusy(true);

        Dictionary<string, AccountScanResult> scanResults;
        try
        {
            var progress = new Progress<long>(count => context.SetStatusText($"Scanning ACLs... {count} items"));
            using var cts = new CancellationTokenSource();
            scanResults = await bulkScan.ScanAllAccountsAsync(rootPath, knownSids, progress, cts.Token);
        }
        catch (Exception ex)
        {
            log.Error(context.FailureLogMessage, ex);
            context.ShowScanFailed(ex);
            return;
        }
        finally
        {
            context.SetScanBusy(false);
            context.SetStatusText("Ready");
        }

        var database = databaseProvider.GetDatabase();
        scanResults = resultProcessor.FilterManagedPaths(scanResults, database.Apps, aclService);
        if (scanResults.Count == 0)
        {
            context.ShowNoResults();
            return;
        }

        using var dialog = bulkScanResultDialogFactory.Create(scanResults, sidNameCache);
        if (context.ShowResults(dialog.Form) != DialogResult.OK)
            return;

        var selected = dialog.SelectedResults;
        if (selected.Count == 0)
            return;

        var summary = resultProcessor.ApplyScanResults(selected, context.SaveImportedResults);
        warningPresenter.ShowSkippedConflictWarning(summary, "Scan ACLs");
    }
}
