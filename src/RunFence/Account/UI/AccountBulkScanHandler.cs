using RunFence.Account.UI.Forms;
using RunFence.Acl;
using RunFence.Acl.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Account.UI;

/// <summary>
/// Handles the bulk ACL scan workflow: scanning a folder tree for ACEs belonging to known accounts,
/// presenting results to the user, and applying selected results to the database.
/// Extracted from <see cref="AccountsPanel"/> to remove a distinct responsibility.
/// </summary>
public class AccountBulkScanHandler(
    IAccountAclBulkScanService bulkScan,
    IAclService aclService,
    ILoggingService log,
    ISidNameCacheService sidNameCache,
    IDatabaseProvider databaseProvider)
{
    public async Task ScanAcls(
        IAccountsPanelContext context,
        ToolStripButton scanButton,
        Label statusLabel)
    {
        using var folderDialog = new FolderBrowserDialog();
        folderDialog.Description = "Select a root folder to scan for ACLs";
        folderDialog.UseDescriptionForTitle = true;
        if (folderDialog.ShowDialog(context.OwnerControl.FindForm()) != DialogResult.OK)
            return;

        var rootPath = folderDialog.SelectedPath;
        if (string.IsNullOrEmpty(rootPath))
            return;

        var knownSids = context.CredentialStore.Credentials
            .Where(c => !string.IsNullOrEmpty(c.Sid))
            .Select(c => c.Sid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (knownSids.Count == 0)
        {
            MessageBox.Show("No known accounts to scan for.", "Scan ACLs", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        statusLabel.Text = "Scanning ACLs...";
        scanButton.Enabled = false;

        Dictionary<string, AccountScanResult> scanResults;
        try
        {
            var progress = new Progress<long>(count => statusLabel.Text = $"Scanning ACLs... {count} items");
            using var cts = new CancellationTokenSource();
            scanResults = await bulkScan.ScanAllAccountsAsync(rootPath, knownSids, progress, cts.Token);
        }
        catch (Exception ex)
        {
            log.Error("ACL bulk scan failed", ex);
            MessageBox.Show($"Scan failed: {ex.Message}", "Scan ACLs", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            scanButton.Enabled = true;
            statusLabel.Text = "Ready";
        }

        scanResults = FilterManagedPaths(scanResults, context.Database.Apps, aclService);

        if (scanResults.Count == 0)
        {
            MessageBox.Show("No ACL entries found for the known accounts in the selected folder.", "Scan ACLs", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new AclBulkScanResultDialog(scanResults, sidNameCache);
        if (context.ShowModal(dialog) != DialogResult.OK)
            return;

        var selected = dialog.SelectedResults;
        if (selected.Count == 0)
            return;

        ApplyScanResults(selected, () => context.SaveAndRefresh());
    }

    /// <summary>
    /// Removes from <paramref name="results"/> any grants or traverse paths that fall under
    /// a path managed by an app entry (i.e. <see cref="AppEntry.RestrictAcl"/> is true).
    /// App-entry managed ACLs are owned by the ACL enforcement system and should not be
    /// imported into AccountGrants via the bulk scan.
    /// </summary>
    public static Dictionary<string, AccountScanResult> FilterManagedPaths(
        Dictionary<string, AccountScanResult> results,
        IReadOnlyList<AppEntry> apps,
        IAclService aclService)
    {
        var managedPaths = apps
            .Where(a => a is { RestrictAcl: true, IsUrlScheme: false })
            .Select(a => AclHelper.NormalizePath(aclService.ResolveAclTargetPath(a)))
            .ToList();

        if (managedPaths.Count == 0)
            return results;

        bool IsManaged(string path) => managedPaths.Any(m => AclHelper.PathIsAtOrBelow(path, m));

        var filtered = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sid, result) in results)
        {
            var grants = result.Grants.Where(g => !IsManaged(g.Path)).ToList();
            var traverses = result.TraversePaths.Where(p => !IsManaged(p)).ToList();
            if (grants.Count > 0 || traverses.Count > 0)
                filtered[sid] = new AccountScanResult(grants, traverses);
        }

        return filtered;
    }

    public void ApplyScanResults(
        Dictionary<string, AccountScanResult> selected,
        Action saveDatabase)
    {
        var database = databaseProvider.GetDatabase();
        foreach (var (sid, result) in selected)
        {
            var grants = database.GetOrCreateAccount(sid).Grants;

            foreach (var grant in result.Grants)
            {
                var normalizedPath = grant.Path.TrimEnd('\\');
                bool alreadyExists = grants.Any(e =>
                    string.Equals(e.Path.TrimEnd('\\'), normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                    e.IsDeny == grant.IsDeny &&
                    !e.IsTraverseOnly);

                if (alreadyExists)
                    continue;

                // Allow mode: Own=true if this SID is the path owner.
                // Deny mode: Own represents "admin should own". We conservatively set Own=false
                // (don't claim admin ownership intent from scan; user can set it in ACL Manager).
                var savedRights = grant.IsDeny
                    ? SavedRightsState.DefaultForMode(true) with { Execute = grant.Execute, Read = grant.Read }
                    : SavedRightsState.DefaultForMode(false, own: grant.IsOwner) with { Execute = grant.Execute, Write = grant.Write, Special = grant.Special };

                grants.Add(new GrantedPathEntry
                {
                    Path = normalizedPath,
                    IsDeny = grant.IsDeny,
                    IsTraverseOnly = false,
                    SavedRights = savedRights
                });
            }

            foreach (var traversePath in result.TraversePaths)
            {
                var normalizedPath = traversePath.TrimEnd('\\');
                bool alreadyExists = grants.Any(e =>
                    string.Equals(e.Path.TrimEnd('\\'), normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                    e.IsTraverseOnly);

                if (alreadyExists)
                    continue;

                grants.Add(new GrantedPathEntry
                {
                    Path = normalizedPath,
                    IsTraverseOnly = true
                });
            }
        }

        saveDatabase();
    }
}