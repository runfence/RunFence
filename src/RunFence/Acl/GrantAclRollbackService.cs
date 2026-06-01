using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl;

public sealed class GrantAclRollbackService(
    ITraverseCoreOperations traverseCore,
    GrantFileSystemOperations fileSystemOperations,
    IPathSecurityDescriptorAccessor aclAccessor,
    GrantRuntimeMutationService grantRuntimeMutationService,
    GrantRuntimeSnapshotService grantRuntimeSnapshotService,
    TraverseGrantStateService traverseGrantStateService)
{
    public readonly record struct OwnerRollbackState(bool OwnerMayHaveChanged, string? OriginalOwnerSid);

    public OwnerRollbackState CaptureOwnerRollbackState(string path, string? ownerSid, bool shouldResetOwner)
    {
        var ownerMayHaveChanged = ownerSid != null || shouldResetOwner;
        if (!ownerMayHaveChanged)
            return new OwnerRollbackState(false, null);

        var security = aclAccessor.GetSecurity(path);
        var ownerIdentity = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        return new OwnerRollbackState(true, ownerIdentity?.Value);
    }

    public bool TryRestoreTargetSecuritySnapshot(
        string normalizedPath,
        FileSystemSecurity? previousTargetSecurity,
        string? primaryConfigPath,
        GrantOperationException operationException)
    {
        if (previousTargetSecurity == null)
            return false;

        try
        {
            aclAccessor.SetOwnerAndAclWithFallback(normalizedPath, previousTargetSecurity);
            return true;
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.GrantAclRollback,
                normalizedPath,
                primaryConfigPath,
                ex);
        }

        return false;
    }

    public void TryRollbackGrantAcl(
        string accountSid,
        string normalizedPath,
        GrantedPathEntry? priorEntry,
        GrantedPathEntry newEntry,
        OwnerRollbackState ownerRollbackState,
        string? primaryConfigPath,
        GrantOperationException operationException,
        bool removeNewEntryBeforeRestore)
    {
        try
        {
            if (priorEntry == null)
            {
                grantRuntimeMutationService.RemoveRuntimeGrant(accountSid, normalizedPath, newEntry, updateFileSystem: true);
            }
            else if (priorEntry.IsDeny == newEntry.IsDeny)
            {
                if (grantRuntimeMutationService.HasRuntimeGrantEntry(accountSid, normalizedPath, priorEntry.IsDeny))
                {
                    fileSystemOperations.UpdateGrant(
                        accountSid,
                        normalizedPath,
                        priorEntry.IsDeny,
                        priorEntry.SavedRights ?? SavedRightsState.DefaultForMode(priorEntry.IsDeny),
                        ResolveOwnerSid(accountSid, priorEntry.IsDeny, priorEntry.SavedRights),
                        desiredPreviousSaclLabel: priorEntry.PreviousSaclLabel);
                }
                else
                {
                    fileSystemOperations.AddGrant(
                        accountSid,
                        normalizedPath,
                        priorEntry.IsDeny,
                        priorEntry.SavedRights,
                        ResolveOwnerSid(accountSid, priorEntry.IsDeny, priorEntry.SavedRights),
                        desiredPreviousSaclLabel: priorEntry.PreviousSaclLabel);
                    if (!priorEntry.IsDeny)
                    {
                        grantRuntimeMutationService.ApplyAllowGrantSideEffects(
                            accountSid,
                            normalizedPath,
                            priorEntry.SavedRights ?? SavedRightsState.DefaultForMode(isDeny: false));
                    }
                }
            }
            else
            {
                if (removeNewEntryBeforeRestore)
                    grantRuntimeMutationService.RemoveRuntimeGrant(accountSid, normalizedPath, newEntry, updateFileSystem: true);
                fileSystemOperations.AddGrant(
                    accountSid,
                    normalizedPath,
                    priorEntry.IsDeny,
                    priorEntry.SavedRights,
                    ResolveOwnerSid(accountSid, priorEntry.IsDeny, priorEntry.SavedRights),
                    desiredPreviousSaclLabel: priorEntry.PreviousSaclLabel);
                if (!priorEntry.IsDeny)
                {
                    grantRuntimeMutationService.ApplyAllowGrantSideEffects(
                        accountSid,
                        normalizedPath,
                        priorEntry.SavedRights ?? SavedRightsState.DefaultForMode(isDeny: false));
                }
            }
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.GrantAclRollback,
                normalizedPath,
                primaryConfigPath,
                ex);
        }

        try
        {
            if (priorEntry == null)
                RestoreCapturedOwner(normalizedPath, ownerRollbackState);
            else
                RestoreOwnerAfterRollback(normalizedPath, priorEntry, ownerRollbackState);
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.GrantAclRollback,
                normalizedPath,
                primaryConfigPath,
                ex);
        }
    }

    public void TryRollbackTraverseAcesAfterSnapshotRestore(
        GrantRuntimeEntrySnapshot snapshot,
        string restoredTargetPath,
        GrantOperationException operationException)
    {
        try
        {
            var currentEntry = grantRuntimeSnapshotService.CaptureTraverseSnapshot(
                snapshot.Sid,
                snapshot.Path).Entry;

            var currentPaths = currentEntry == null
                ? []
                : traverseGrantStateService.CollectStoredTraversePaths(currentEntry);
            var previousPaths = snapshot.Entry == null
                ? []
                : traverseGrantStateService.CollectStoredTraversePaths(snapshot.Entry);
            var rollbackPaths = currentPaths
                .Where(path => !previousPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                .Where(path => !string.Equals(path, restoredTargetPath, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (rollbackPaths.Count == 0)
                return;

            traverseCore.RemoveTraverseAces(snapshot.Sid, rollbackPaths);
        }
        catch (Exception ex)
        {
            operationException.AppendCleanupFailure(
                GrantApplyFailureStep.TraverseAclRollback,
                snapshot.Path,
                null,
                ex);
        }
    }

    private static string? ResolveOwnerSid(string accountSid, bool isDeny, SavedRightsState? savedRights)
    {
        if (isDeny || savedRights?.Own != true || !AclHelper.CanAssignGrantOwner(accountSid))
            return null;

        return accountSid;
    }

    private void RestoreOwnerAfterRollback(
        string path,
        GrantedPathEntry priorEntry,
        OwnerRollbackState ownerRollbackState)
    {
        if (RestoreCapturedOwner(path, ownerRollbackState))
            return;

        var rights = priorEntry.SavedRights ?? SavedRightsState.DefaultForMode(priorEntry.IsDeny);
        if (!ownerRollbackState.OwnerMayHaveChanged && !(priorEntry.IsDeny && rights.Own))
            return;

        if (priorEntry.IsDeny && rights.Own)
            fileSystemOperations.ResetOwner(path, recursive: false);
    }

    private bool RestoreCapturedOwner(string path, OwnerRollbackState ownerRollbackState)
    {
        if (string.IsNullOrEmpty(ownerRollbackState.OriginalOwnerSid))
            return false;

        fileSystemOperations.ChangeOwner(path, ownerRollbackState.OriginalOwnerSid, recursive: false);
        return true;
    }
}
