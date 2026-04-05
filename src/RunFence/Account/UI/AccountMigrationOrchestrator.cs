using RunFence.Account.OrphanedProfiles;
using RunFence.Account.UI.OrphanedProfiles;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.SidMigration;
using RunFence.SidMigration.UI.Forms;
using RunFence.UI.Forms;

namespace RunFence.Account.UI;

public class AccountMigrationOrchestrator(
    ISidMigrationService sidMigrationService,
    Func<InAppMigrationHandler> createMigrationHandler,
    IOrphanedProfileService orphanedProfileService,
    IAccountLifecycleManager lifecycleManager,
    ILocalUserProvider localUserProvider,
    ILoggingService log,
    ISidResolver sidResolver,
    ISidNameCacheService sidNameCache) : IDisposable
{
    private CancellationTokenSource? _aclCleanupCts;
    private readonly List<string> _pendingAclCleanupSids = new();

    public void Dispose()
    {
        _aclCleanupCts?.Cancel();
        _aclCleanupCts?.Dispose();
        _aclCleanupCts = null;
    }

    public void MigrateSids(SessionContext session, Form? parent, Action onMigrationApplied)
    {
        using var dlg = new SidMigrationDialog(session, sidMigrationService, createMigrationHandler(), localUserProvider, log, sidResolver, sidNameCache);
        DataPanel.ShowModal(dlg, parent);
        if (dlg.InAppMigrationApplied)
            onMigrationApplied();
    }

    public void DeleteProfiles(Form? parent)
    {
        using var dlg = new OrphanedProfilesDialog(orphanedProfileService);
        DataPanel.ShowModal(dlg, parent);
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

        var (fixedCount, error) = await lifecycleManager.CleanupAclReferencesAsync(sidsToClean, progress, ct);

        if (!ct.IsCancellationRequested && isAlive())
        {
            foreach (var s in sidsToClean)
                _pendingAclCleanupSids.Remove(s);

            if (error != null)
                setStatus("ACL cleanup failed");
            else
                setStatus(fixedCount > 0
                    ? $"ACL cleanup done: {fixedCount} reference{(fixedCount == 1 ? "" : "s")} fixed"
                    : "ACL cleanup done: no orphaned references found");
        }
    }
}