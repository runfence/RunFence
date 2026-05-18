using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Groups;

public class GroupDeletionService(
    ILocalGroupMutationService groupMembership,
    ISessionProvider sessionProvider,
    IAclService aclService,
    IPathGrantService pathGrantService,
    ISessionSaver? sessionSaver,
    ILoggingService log)
{
    public GroupDeletionResult DeleteGroup(string sid)
    {
        var database = sessionProvider.GetSession().Database;
        var preDeleteSnapshot = database.CreateSnapshot();
        database.SidNames.TryGetValue(sid, out var groupName);

        try
        {
            groupMembership.DeleteGroup(sid);
        }
        catch (Exception ex)
        {
            database.ReplaceWithSnapshot(preDeleteSnapshot);
            log.Error($"Failed to delete group {sid}", ex);
            return new GroupDeletionResult(
                GroupDeletionStatus.OsDeleteFailed,
                sid,
                groupName,
                [],
                false,
                [],
                [$"Failed to delete group from OS: {ex.Message}"]);
        }

        var changedAppIds = database.Apps
            .Where(a => a.RestrictAcl && !a.IsUrlScheme &&
                        a.AllowedAclEntries?.Any(e => SidComparer.SidEquals(e.Sid, sid)) == true)
            .Select(a => a.Id)
            .ToList();

        var warnings = new List<string>();
        var errors = new List<string>();
        var allAppsBefore = database.Apps.ToList();
        var affectedApps = allAppsBefore
            .Where(a => changedAppIds.Contains(a.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var app in affectedApps)
        {
            try
            {
                aclService.RevertAcl(app, allAppsBefore);
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to revert ACL for app '{app.Name}' after deleting group {sid}: {ex.Message}");
                log.Warn($"Failed to revert ACL for deleted group '{sid}': {ex.Message}");
            }
        }

        var grantsToRemove = database.GetAccount(sid)?.Grants
            .Select(grant => grant.Clone())
            .ToList() ?? [];

        foreach (var grant in grantsToRemove)
        {
            try
            {
                var mode = grant.IsDeny ? "deny" : "allow";

                if (grant.IsTraverseOnly)
                {
                    var removeResult = pathGrantService.RemoveTraverse(sid, grant.Path);
                    AppendGrantCleanupWarnings(warnings, removeResult.Warnings);
                    if (!removeResult.DatabaseModified)
                    {
                        throw new InvalidOperationException(
                            $"Cleanup did not find traverse entry '{grant.Path}' for SID '{sid}'.");
                    }
                }
                else
                {
                    var removeResult = pathGrantService.RemoveGrant(sid, grant.Path, grant.IsDeny);
                    AppendGrantCleanupWarnings(warnings, removeResult.Warnings);
                    if (!removeResult.DatabaseModified)
                    {
                        throw new InvalidOperationException(
                            $"Cleanup did not find {mode} grant '{grant.Path}' for SID '{sid}'.");
                    }
                }
            }
            catch (Exception ex)
            {
                var mode = grant.IsDeny ? "deny" : "allow";
                if (grant.IsTraverseOnly)
                    mode = "traverse";
                warnings.Add($"Failed to remove {mode} grant '{grant.Path}' for deleted group {sid}: {ex.Message}");
                log.Warn($"Failed to remove {mode} grant '{grant.Path}' after deleting group {sid}: {ex.Message}");
            }
        }

        CleanupDeletedGroupReferences(database, sid);

        var allAppsAfter = database.Apps.ToList();
        var affectedAppsAfter = allAppsAfter
            .Where(a => changedAppIds.Contains(a.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var app in affectedAppsAfter)
        {
            try
            {
                aclService.ApplyAcl(app, allAppsAfter);
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to re-apply ACL for app '{app.Name}' after deleting group {sid}: {ex.Message}");
                log.Warn($"Failed to re-apply ACL for deleted group '{sid}': {ex.Message}");
            }
        }

        try
        {
            aclService.RecomputeAllAncestorAcls(allAppsAfter);
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to recompute ancestor ACLs after deleting group '{sid}': {ex.Message}");
            log.Warn($"Failed to recompute ancestor ACLs after deleting group '{sid}': {ex.Message}");
        }

        try
        {
            sessionSaver?.SaveConfig();
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to save config after group deletion: {ex.Message}");
            log.Error($"Failed to save config after group cleanup for {sid}", ex);
            return new GroupDeletionResult(
                GroupDeletionStatus.WindowsDeletedSaveFailed,
                sid,
                groupName,
                changedAppIds,
                DataChangedRaised: true,
                warnings,
                errors,
                SaveErrorMessage: ex.Message);
        }

        return new GroupDeletionResult(
            GroupDeletionStatus.Succeeded,
            sid,
            groupName,
            changedAppIds,
            DataChangedRaised: true,
            warnings,
            errors);
    }

    private static void CleanupDeletedGroupReferences(AppDatabase database, string sid)
    {
        foreach (var app in database.Apps)
            app.AllowedAclEntries?.RemoveAll(entry => SidComparer.SidEquals(entry.Sid, sid));

        foreach (var account in database.Accounts)
            RemoveSidReferencesFromGrants(account.Grants, sid);

        if (database.AccountGroupSnapshots != null)
        {
            database.AccountGroupSnapshots.Remove(sid);
            foreach (var groups in database.AccountGroupSnapshots.Values)
                groups.RemoveAll(groupSid => SidComparer.SidEquals(groupSid, sid));
        }

        GroupDatabaseHelper.CleanupDeletedGroupData(sid, database);

        database.SidNames.Remove(sid);
    }

    private static void RemoveSidReferencesFromGrants(List<GrantedPathEntry> grants, string sid)
    {
        foreach (var grant in grants)
        {
            if (grant.SourceSids == null)
                continue;

            grant.SourceSids.RemoveAll(sourceSid => SidComparer.SidEquals(sourceSid, sid));
            if (grant.SourceSids.Count == 0)
                grant.SourceSids = null;
        }
    }

    private static void AppendGrantCleanupWarnings(List<string> warnings, IReadOnlyList<GrantApplyWarning> grantWarnings)
    {
        foreach (var warning in grantWarnings)
            warnings.Add(GrantApplyFailureFormatter.Format(warning));
    }
}
