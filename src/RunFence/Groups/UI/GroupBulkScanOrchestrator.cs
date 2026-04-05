using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Acl;
using RunFence.Acl.UI.Forms;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.UI.Forms;

namespace RunFence.Groups.UI;

/// <summary>
/// Handles the bulk ACL scan workflow for group SIDs.
/// Builds the known-SIDs set from local groups (instead of CredentialStore used by AccountBulkScanHandler).
/// </summary>
public class GroupBulkScanOrchestrator(
    IAccountAclBulkScanService bulkScan,
    ILocalGroupMembershipService groupMembership,
    IAclService aclService,
    ISidNameCacheService sidNameCache,
    ILoggingService log,
    AccountBulkScanHandler bulkScanHandler,
    IDatabaseProvider databaseProvider)
{
    public async Task ScanAcls(
        IWin32Window owner,
        Action<bool> setScanButtonEnabled,
        Action<string> setStatusText,
        Action saveDatabase)
    {
        using var folderDialog = new FolderBrowserDialog();
        folderDialog.Description = "Select a root folder to scan for ACLs";
        folderDialog.UseDescriptionForTitle = true;
        if (folderDialog.ShowDialog(owner) != DialogResult.OK)
            return;

        var rootPath = folderDialog.SelectedPath;
        if (string.IsNullOrEmpty(rootPath))
            return;

        var knownSids = await Task.Run(() =>
            groupMembership.GetLocalGroups()
                .Where(g => !string.IsNullOrEmpty(g.Sid))
                .Select(g => g.Sid)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));

        if (knownSids.Count == 0)
        {
            MessageBox.Show("No local groups to scan for.", "Scan ACLs", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        setStatusText("Scanning ACLs...");
        setScanButtonEnabled(false);

        Dictionary<string, AccountScanResult> scanResults;
        try
        {
            var progress = new Progress<long>(count => setStatusText($"Scanning ACLs... {count} items"));
            using var cts = new CancellationTokenSource();
            scanResults = await bulkScan.ScanAllAccountsAsync(rootPath, knownSids, progress, cts.Token);
        }
        catch (Exception ex)
        {
            log.Error("Group ACL bulk scan failed", ex);
            MessageBox.Show($"Scan failed: {ex.Message}", "Scan ACLs", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            setScanButtonEnabled(true);
            setStatusText("Ready");
        }

        var database = databaseProvider.GetDatabase();
        scanResults = AccountBulkScanHandler.FilterManagedPaths(scanResults, database.Apps, aclService);

        if (scanResults.Count == 0)
        {
            MessageBox.Show("No ACL entries found for the local groups in the selected folder.", "Scan ACLs",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new AclBulkScanResultDialog(scanResults, sidNameCache);
        if (DataPanel.ShowModal(dialog, owner) != DialogResult.OK)
            return;

        var selected = dialog.SelectedResults;
        if (selected.Count == 0)
            return;

        bulkScanHandler.ApplyScanResults(selected, saveDatabase);
    }
}