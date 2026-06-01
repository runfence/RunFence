using System.Security.AccessControl;
using System.Security.Principal;
using System.Linq;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Acl.Traverse;

/// <summary>
/// Performs per-SID traverse grant reconciliation: adds traverse ACEs when needed
/// and removes them when they become redundant due to group membership changes.
/// Used by <see cref="GrantReconciliationService"/> during the background reconciliation phase.
/// </summary>
public class SidReconciler(
    IAclPermissionService aclPermission,
    Func<AncestorTraverseGranter> ancestorTraverseGranterFactory,
    ILoggingService log,
    IInteractiveUserResolver interactiveUserResolver,
    IFileSystemPathInfo pathInfo,
    IProgramDataDirectoryProvisioningService programDataDirectoryProvisioningService,
    IProgramDataKnownPathResolver programDataKnownPathResolver)
{
    public readonly record struct SidReconciliationResult(
        string Sid,
        List<string> NewGroups,
        bool Succeeded,
        List<(string Path, List<string> AppliedPaths)> NewTraverseEntries,
        HashSet<string> RemovedTraversePaths,
        string? ErrorMessage);

    private readonly IProgramDataDirectoryProvisioningService _programDataDirectoryProvisioningService = programDataDirectoryProvisioningService;
    private readonly IProgramDataKnownPathResolver _programDataKnownPathResolver = programDataKnownPathResolver;

    /// <summary>
    /// Reconciles traverse grants for a single SID given its new group memberships.
    /// Runs on the background thread and returns a self-contained result.
    /// </summary>
    public SidReconciliationResult ReconcileSid(
        string sid,
        IReadOnlyList<string> newGroups,
        IReadOnlyDictionary<string, IReadOnlyList<GrantedPathEntry>>? accountGrants)
    {
        var newTraverseEntries = new List<(string Path, List<string> AppliedPaths)>();
        var removedTraversePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var identity = new SecurityIdentifier(sid);

            ReconcileLogonScript(sid, identity, newGroups, newTraverseEntries, removedTraversePaths, accountGrants);

            if (string.Equals(sid, interactiveUserResolver.GetInteractiveUserSid(), StringComparison.OrdinalIgnoreCase))
                ReconcileAppDirectory(sid, identity, newGroups, newTraverseEntries, removedTraversePaths, accountGrants);

            ReconcileDragBridgeTempRoot(sid, identity, newGroups, newTraverseEntries, removedTraversePaths, accountGrants);

            return new SidReconciliationResult(
                sid,
                newGroups.ToList(),
                true,
                newTraverseEntries,
                removedTraversePaths,
                null);
        }
        catch (Exception ex)
        {
            log.Warn($"SidReconciler: reconciliation failed for '{sid}': {ex.Message}");
            return new SidReconciliationResult(
                sid,
                newGroups.ToList(),
                false,
                newTraverseEntries,
                removedTraversePaths,
                ex.Message);
        }
    }

    private void ReconcileLogonScript(string sid, SecurityIdentifier identity, IReadOnlyList<string> groupSids,
        List<(string, List<string>)> newTraverseEntries,
        HashSet<string> removedTraversePaths,
        IReadOnlyDictionary<string, IReadOnlyList<GrantedPathEntry>>? accountGrants)
    {
        var scriptsDir = _programDataKnownPathResolver.GetDirectoryPath(ProgramDataPolicies.Scripts);
        var scriptFile = Path.Combine(scriptsDir, $"{sid}_block_login.cmd");
        if (!pathInfo.DirectoryExists(scriptsDir) || !pathInfo.FileExists(scriptFile))
            return;

            _programDataDirectoryProvisioningService.EnsureKnownDirectory(
                ProgramDataPolicies.Scripts);

        ReconcileTraverseLocation(identity, groupSids, scriptsDir, scriptFile,
            newTraverseEntries, removedTraversePaths, accountGrants, sid);
    }

    private void ReconcileAppDirectory(string sid, SecurityIdentifier identity, IReadOnlyList<string> groupSids,
        List<(string, List<string>)> newTraverseEntries,
        HashSet<string> removedTraversePaths,
        IReadOnlyDictionary<string, IReadOnlyList<GrantedPathEntry>>? accountGrants)
    {
        var appDir = Path.GetDirectoryName(PathConstants.UnlockCmdPath);
        if (string.IsNullOrEmpty(appDir))
            return;
        ReconcileTraverseLocation(identity, groupSids, appDir, null,
            newTraverseEntries, removedTraversePaths, accountGrants, sid);
    }

    private void ReconcileDragBridgeTempRoot(string sid, SecurityIdentifier identity, IReadOnlyList<string> groupSids,
        List<(string, List<string>)> newTraverseEntries,
        HashSet<string> removedTraversePaths,
        IReadOnlyDictionary<string, IReadOnlyList<GrantedPathEntry>>? accountGrants)
    {
        var tempRoot = _programDataKnownPathResolver.GetDirectoryPath(ProgramDataPolicies.DragBridge);
        if (pathInfo.DirectoryExists(tempRoot))
        {
            _programDataDirectoryProvisioningService.EnsureKnownDirectory(
                ProgramDataPolicies.DragBridge);
        }

        ReconcileTraverseLocation(identity, groupSids, tempRoot, null,
            newTraverseEntries, removedTraversePaths, accountGrants, sid);
    }

    private void ReconcileTraverseLocation(
        SecurityIdentifier identity,
        IReadOnlyList<string> groupSids,
        string dirPath,
        string? prerequisiteFilePath,
        List<(string, List<string>)> newTraverseEntries,
        HashSet<string> removedTraversePaths,
        IReadOnlyDictionary<string, IReadOnlyList<GrantedPathEntry>>? accountGrants,
        string sid)
    {
        if (!pathInfo.DirectoryExists(dirPath))
            return;
        if (prerequisiteFilePath != null && !pathInfo.FileExists(prerequisiteFilePath))
            return;

        var granter = ancestorTraverseGranterFactory();
        var (appliedPaths, anyAceAdded) = granter.GrantOnPathAndAncestors(dirPath, identity, groupSids: groupSids);
        if (anyAceAdded)
        {
            EnsureTraverseEffectiveOnTrackedPaths(sid, groupSids, appliedPaths);
            newTraverseEntries.Add((dirPath, appliedPaths));
        }

        CheckRedundantTraverse(sid, dirPath, groupSids, granter, removedTraversePaths, accountGrants);
    }

    /// <summary>
    /// Checks whether a direct traverse entry is redundant by removing only that managed direct
    /// traverse ACE from an in-memory security copy and re-evaluating effective rights for the
    /// real account SID plus its groups.
    /// </summary>
    private void CheckRedundantTraverse(
        string sid,
        string path,
        IReadOnlyList<string> groupSids,
        AncestorTraverseGranter granter,
        HashSet<string> removedTraversePaths,
        IReadOnlyDictionary<string, IReadOnlyList<GrantedPathEntry>>? accountGrants)
    {
        if (accountGrants == null || !accountGrants.TryGetValue(sid, out var entries))
            return;

        var traverseRights = TraverseRightsHelper.TraverseRights;
        var effectiveGroupSids = AclComputeHelper.ExcludeAdministratorsGroup(groupSids);
        var normalizedPath = Path.GetFullPath(path);

        foreach (var entry in entries)
        {
            if (!entry.IsTraverseOnly)
                continue;
            if (!string.Equals(Path.GetFullPath(entry.Path), normalizedPath, StringComparison.OrdinalIgnoreCase))
                continue;

            var pathsToCheck = entry.AllAppliedPaths ?? [entry.Path];
            bool stillEffectiveWithoutDirectAce = true;

            foreach (var appliedPath in pathsToCheck)
            {
                try
                {
                    if (!pathInfo.DirectoryExists(appliedPath))
                        continue;

                    var dirSecurity = pathInfo.GetDirectorySecurity(appliedPath);
                    var identity = new SecurityIdentifier(sid);
                    var hasDirectTraverseDeny = dirSecurity
                        .GetAccessRules(true, false, typeof(SecurityIdentifier))
                        .OfType<FileSystemAccessRule>()
                        .Any(rule =>
                            rule.AccessControlType == AccessControlType.Deny &&
                            rule.IdentityReference is SecurityIdentifier ruleSid &&
                            ruleSid.Equals(identity) &&
                            GrantRightsMapper.IsTraverseOnly(rule.FileSystemRights) &&
                            rule.InheritanceFlags == InheritanceFlags.None);
                    if (hasDirectTraverseDeny)
                    {
                        stillEffectiveWithoutDirectAce = false;
                        break;
                    }

                    var modifiedSecurity = RemoveManagedTraverseAce(dirSecurity, sid);
                    if (!aclPermission.HasEffectiveRights(modifiedSecurity, sid, effectiveGroupSids, traverseRights))
                    {
                        stillEffectiveWithoutDirectAce = false;
                        break;
                    }
                }
                catch
                {
                    stillEffectiveWithoutDirectAce = false;
                    break;
                }
            }

            if (stillEffectiveWithoutDirectAce)
            {
                removedTraversePaths.Add(normalizedPath);

                var identity = new SecurityIdentifier(sid);
                foreach (var appliedPath in pathsToCheck)
                {
                    try
                    {
                        granter.RemoveAce(appliedPath, identity);
                    }
                    catch (Exception ex)
                    {
                        log.Warn($"Failed to remove redundant traverse ACE on '{appliedPath}': {ex.Message}");
                    }
                }

                EnsureDirectTraverseRemoved(pathsToCheck, sid);
            }

            break;
        }
    }

    private void EnsureTraverseEffectiveOnTrackedPaths(
        string sid,
        IReadOnlyList<string> groupSids,
        IEnumerable<string> appliedPaths)
    {
        var effectiveGroupSids = AclComputeHelper.ExcludeAdministratorsGroup(groupSids);

        foreach (var appliedPath in appliedPaths)
        {
            if (!pathInfo.DirectoryExists(appliedPath))
                continue;

            var security = pathInfo.GetDirectorySecurity(appliedPath);
            if (!aclPermission.HasEffectiveRights(security, sid, effectiveGroupSids, TraverseRightsHelper.TraverseRights))
            {
                throw new InvalidOperationException(
                    $"Traverse reconciliation failed to make '{appliedPath}' effective for '{sid}'.");
            }
        }
    }

    private void EnsureDirectTraverseRemoved(IEnumerable<string> appliedPaths, string sid)
    {
        var identity = new SecurityIdentifier(sid);

        foreach (var appliedPath in appliedPaths)
        {
            if (!pathInfo.DirectoryExists(appliedPath))
                continue;

            var security = pathInfo.GetDirectorySecurity(appliedPath);
            var hasDirectTraverseAllow = security
                .GetAccessRules(true, false, typeof(SecurityIdentifier))
                .OfType<FileSystemAccessRule>()
                .Any(rule =>
                    rule.AccessControlType == AccessControlType.Allow &&
                    rule.IdentityReference is SecurityIdentifier ruleSid &&
                    ruleSid.Equals(identity) &&
                    GrantRightsMapper.IsTraverseOnly(rule.FileSystemRights) &&
                    rule.InheritanceFlags == InheritanceFlags.None);
            if (hasDirectTraverseAllow)
            {
                throw new InvalidOperationException(
                    $"Traverse reconciliation failed to remove redundant traverse ACE on '{appliedPath}' for '{sid}'.");
            }
        }
    }

    private static FileSystemSecurity RemoveManagedTraverseAce(FileSystemSecurity security, string sid)
    {
        var clone = new DirectorySecurity();
        clone.SetSecurityDescriptorBinaryForm(security.GetSecurityDescriptorBinaryForm());

        var identity = new SecurityIdentifier(sid);
        foreach (FileSystemAccessRule rule in clone.GetAccessRules(true, false, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow)
                continue;
            if (rule.IdentityReference is not SecurityIdentifier ruleSid || !ruleSid.Equals(identity))
                continue;
            if (!GrantRightsMapper.IsTraverseOnly(rule.FileSystemRights) ||
                rule.InheritanceFlags != InheritanceFlags.None)
                continue;

            clone.RemoveAccessRuleSpecific(rule);
            break;
        }

        return clone;
    }

}
