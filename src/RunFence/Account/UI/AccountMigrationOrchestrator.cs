using RunFence.Account.OrphanedProfiles;
using RunFence.Account.UI.OrphanedProfiles;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.SidMigration.UI.Forms;

namespace RunFence.Account.UI;

public class AccountMigrationOrchestrator(
    IModalCoordinator modalCoordinator,
    SidMigrationDialogFactory sidMigrationDialogFactory,
    IOrphanedProfileService orphanedProfileService,
    IAccountLifecycleManager lifecycleManager,
    IAccountMessageBoxService messageBoxService) : IDisposable
{
    private CancellationTokenSource? _aclCleanupCts;
    private readonly List<string> _pendingAclCleanupSids = new();
    private bool _isOpeningOrphanedProfilesDialog;

    public void Dispose()
    {
        _aclCleanupCts?.Cancel();
        _aclCleanupCts?.Dispose();
        _aclCleanupCts = null;
    }

    public void MigrateSids(Form? parent, Action onMigrationApplied)
    {
        using var dlg = sidMigrationDialogFactory.Create();
        if (modalCoordinator.ShowModal(dlg, parent) == DialogResult.OK)
            onMigrationApplied();
    }

    public async void DeleteProfiles(Form? parent)
    {
        if (_isOpeningOrphanedProfilesDialog)
            return;

        _isOpeningOrphanedProfilesDialog = true;
        try
        {
            List<OrphanedProfile> profiles;
            try
            {
                if (parent != null)
                    parent.UseWaitCursor = true;

                profiles = await Task.Run(orphanedProfileService.GetOrphanedProfiles);

                if (parent != null && parent.IsDisposed)
                    return;
            }
            catch
            {
                if (parent != null && parent.IsDisposed)
                    return;

                using var dlg = OrphanedProfilesDialog.CreateScanErrorDialog(orphanedProfileService);
                modalCoordinator.ShowModal(dlg, parent);
                return;
            }
            finally
            {
                if (parent != null)
                    parent.UseWaitCursor = false;
            }

            if (parent != null && parent.IsDisposed)
                return;

            if (profiles.Count == 0)
            {
                messageBoxService.Show(
                    parent,
                    "No orphaned profiles found.",
                    "Delete Orphaned Profiles",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            using var dialog = new OrphanedProfilesDialog(orphanedProfileService, profiles);
            modalCoordinator.ShowModal(dialog, parent);
        }
        finally
        {
            _isOpeningOrphanedProfilesDialog = false;
        }
    }

    public async Task StartBackgroundAclCleanupAsync(string sid, Action<string> setStatus, Func<bool> isAlive)
    {
        _aclCleanupCts?.Cancel();
        _aclCleanupCts?.Dispose();

        _pendingAclCleanupSids.Add(sid);
        var sidsToClean = new List<string>(_pendingAclCleanupSids);

        _aclCleanupCts = new CancellationTokenSource();
        var ct = _aclCleanupCts.Token;

        var progress = new Progress<AclCleanupProgress>(p =>
        {
            if (!isAlive())
                return;
            var shortPath = p.CurrentPath.Length > 60
                ? "..." + p.CurrentPath[^57..]
                : p.CurrentPath;
            setStatus($"Scanning {shortPath}... ({p.ObjectsFixed} fixed)");
        });

        var cleanupResult = await lifecycleManager.CleanupAclReferencesAsync(sidsToClean, progress, ct);

        if (!ct.IsCancellationRequested && isAlive())
        {
            foreach (var s in sidsToClean)
                _pendingAclCleanupSids.Remove(s);

            if (cleanupResult.ErrorMessage != null)
                setStatus("ACL cleanup failed");
            else
                setStatus(cleanupResult.FixedCount > 0
                    ? $"ACL cleanup done: {cleanupResult.FixedCount} reference{(cleanupResult.FixedCount == 1 ? "" : "s")} fixed"
                    : "ACL cleanup done: no orphaned references found");
        }
    }
}
