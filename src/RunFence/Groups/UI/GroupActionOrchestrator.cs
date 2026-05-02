using RunFence.Acl;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Groups.UI.Forms;
using RunFence.Infrastructure;

namespace RunFence.Groups.UI;

/// <summary>
/// Orchestrates group actions: create/delete group, open ACL Manager, scan ACLs.
/// Does not hold references to grid controls — callers read grid state and pass it as parameters.
/// </summary>
/// <remarks>
/// <paramref name="bulkScanHandler"/> and <paramref name="aclManagerLauncher"/> are nullable
/// because they are unavailable in the foundation scope (pre-session). Available after BeginSessionScope().
/// <paramref name="sessionSaver"/> is nullable for the same reason.
/// </remarks>
public class GroupActionOrchestrator(
    IModalCoordinator modalCoordinator,
    ILocalGroupMembershipService groupMembership,
    GroupBulkScanOrchestrator? bulkScanHandler,
    AccountAclManagerLauncher? aclManagerLauncher,
    ISidNameCacheService sidNameCache,
    ISessionProvider sessionProvider,
    ILoggingService log,
    IAclService aclService,
    IPathGrantService pathGrantService,
    ISessionSaver? sessionSaver = null)
{
    public event Action<string?>? DataChanged;

    public bool IsAclManagerAvailable => aclManagerLauncher != null;

    public bool IsBulkScanAvailable => bulkScanHandler != null;

    public void CreateGroup(IWin32Window? owner)
    {
        using var dlg = new CreateGroupDialog(groupMembership);
        if (modalCoordinator.ShowModal(dlg, owner) != DialogResult.OK)
            return;
        DataChanged?.Invoke(dlg.CreatedGroupSid);
    }

    public void DeleteGroup(string sid, string name)
    {
        var confirm = MessageBox.Show(
            $"Delete group '{name}'?\n\nThis will remove all ACL grants for this group.",
            "Confirm Delete Group", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
            return;

        try
        {
            groupMembership.DeleteGroup(sid);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to delete group {sid}", ex);
            MessageBox.Show($"Failed to delete group: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // OS deletion succeeded — perform three-phase ACL + database cleanup
        var database = sessionProvider.GetSession().Database;
        var allApps = database.Apps.ToList();

        // Phase 1a — Revert filesystem ACEs for apps that granted access to the deleted group SID.
        // Collect affected apps while AllowedAclEntries still contains the deleted SID.
        var affectedApps = allApps
            .Where(a => a.RestrictAcl && !a.IsUrlScheme &&
                        a.AllowedAclEntries?.Any(e =>
                            SidComparer.SidEquals(e.Sid, sid)) == true)
            .ToList();
        foreach (var app in affectedApps)
        {
            try
            {
                aclService.RevertAcl(app, allApps);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to revert ACL for app '{app.Name}' on group deletion: {ex.Message}");
            }
        }

        // Phase 1b — Remove traverse/access grants for the deleted group SID.
        pathGrantService.RemoveAll(sid, updateFileSystem: true);

        // Phase 1c — Remove the deleted group SID from AllowedAclEntries of all apps
        // (must happen before Phase 3 so reapply does not re-add the deleted group's ACEs).
        foreach (var app in allApps)
            app.AllowedAclEntries?.RemoveAll(e =>
                SidComparer.SidEquals(e.Sid, sid));

        // Phase 2 — Database cleanup (group snapshots, account entry grants).
        GroupDatabaseHelper.CleanupDeletedGroupData(sid, database);

        // Phase 3 — Reapply ACEs without the deleted group and recompute ancestor ACLs.
        var appsAfterDeletion = database.Apps.ToList();
        foreach (var app in affectedApps)
        {
            try
            {
                aclService.ApplyAcl(app, appsAfterDeletion);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to re-apply ACL for app '{app.Name}' after group deletion: {ex.Message}");
            }
        }

        try
        {
            aclService.RecomputeAllAncestorAcls(appsAfterDeletion);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to recompute ancestor ACLs after group deletion: {ex.Message}");
        }

        sessionSaver?.SaveConfig();
        DataChanged?.Invoke(null);
    }

    public void OpenAclManager(string sid, IWin32Window? owner)
    {
        if (aclManagerLauncher == null)
            return;

        var displayName = sidNameCache.GetDisplayName(sid);
        aclManagerLauncher.OpenAclManager(sid, displayName, owner);
    }

    public async Task ScanAcls(IWin32Window owner, Action<bool> setScanButtonEnabled, Action<string> setStatusText)
    {
        if (bulkScanHandler == null)
            return;

        await bulkScanHandler.ScanAcls(
            owner,
            setScanButtonEnabled,
            setStatusText,
            () => sessionSaver?.SaveConfig());
    }
}