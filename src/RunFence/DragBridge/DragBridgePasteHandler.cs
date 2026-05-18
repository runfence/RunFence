using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.DragBridge;

/// <summary>
/// Result of <see cref="DragBridgePasteHandler.ResolveFileAccessAsync"/>. Null <see cref="Paths"/> means abort.
/// </summary>
/// <param name="Paths">Final file paths to deliver; null if the operation was aborted.</param>
/// <param name="DatabaseModified">True if any persisted grant or traverse intent changed.</param>
/// <param name="GrantedPaths">Durable paths where ACEs were applied for the target account.</param>
public readonly record struct DragBridgeResolveResult(
    List<string>? Paths,
    bool DatabaseModified,
    List<string> GrantedPaths,
    DragBridgeRollbackPlan? RollbackPlan)
{
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool DurableSaveCompleted { get; init; }
}

/// <summary>
/// Handles the paste side of the DragBridge flow: verifies file access, prompts about
/// inaccessible files, optionally grants ACLs or copies to temp, then sends files over
/// the named pipe to the DragBridge process.
/// </summary>
public class DragBridgePasteHandler(
    IDragBridgeAccessPrompt accessPrompt,
    IDragBridgeTempFileManager tempManager,
    INotificationService notifications,
    ILoggingService log,
    IUiThreadInvoker uiThreadInvoker,
    IAclPermissionService aclPermission,
    IPathGrantService pathGrantService,
    SidDisplayNameResolver displayNameResolver,
    DragBridgeChoiceCache choiceCache) : IDragBridgePasteHandler
{
    public async Task<DragBridgeResolveResult> ResolveFileAccessAsync(
        SecurityIdentifier targetSid,
        SecurityIdentifier? targetContainerSid,
        List<string> filePaths,
        string sourceSid,
        string? sourceContainerSid,
        AppDatabase? database,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return new DragBridgeResolveResult(null, false, [], null);

        var targetSids = GetEffectiveTargetSids(targetSid, targetContainerSid);
        var targetIdentityKey = GetTargetIdentityKey(targetSid, targetContainerSid);

        var existingPaths = filePaths.Where(path => File.Exists(path) || Directory.Exists(path)).ToList();
        if (existingPaths.Count == 0)
        {
            uiThreadInvoker.Invoke(() => notifications.ShowWarning("Drag Bridge",
                $"{filePaths.Count - existingPaths.Count} source file(s) no longer exist."));
            return new DragBridgeResolveResult(null, false, [], null);
        }

        filePaths = existingPaths;

        var sourceSids = GetEffectiveSourceSids(sourceSid, sourceContainerSid);
        var unreadableBySource = filePaths
            .Where(path => sourceSids.Any(sid => aclPermission.NeedsPermissionGrant(path, sid, FileSystemRights.Read)))
            .ToList();
        if (unreadableBySource.Count > 0)
        {
            uiThreadInvoker.Invoke(() => notifications.ShowWarning("Drag Bridge",
                $"Source app cannot read {unreadableBySource.Count} file(s). Cannot continue paste."));
            return new DragBridgeResolveResult(null, false, [], null);
        }

        var inaccessibleByTarget = filePaths
            .Where(path => targetSids.Any(sid => aclPermission.NeedsPermissionGrant(path, sid, FileSystemRights.Read)))
            .ToList();

        var grantedPaths = new List<string>();
        var grantRollbacks = new List<DragBridgeGrantRollbackEntry>();
        var traverseRollbacks = new List<DragBridgeTraverseRollbackEntry>();
        var warnings = new List<string>();
        var dbModified = false;
        var durableSaveCompleted = true;
        if (inaccessibleByTarget.Count == 0)
        {
            return new DragBridgeResolveResult(filePaths, dbModified, grantedPaths, null)
            {
                DurableSaveCompleted = durableSaveCompleted,
                Warnings = warnings
            };
        }

        DragBridgeAccessAction action;
        if (choiceCache.TryGetChoice(targetIdentityKey, inaccessibleByTarget, out var remembered))
        {
            action = remembered;
        }
        else
        {
            var chosen = await AskUserAboutAccessAsync(targetSid, targetContainerSid, inaccessibleByTarget, database, ct);
            if (chosen == null)
                return new DragBridgeResolveResult(null, false, [], null);

            action = chosen.Value;
            if (action == DragBridgeAccessAction.CopyToTemp)
                choiceCache.RememberChoice(targetIdentityKey, inaccessibleByTarget, action);
        }

        switch (action)
        {
            case DragBridgeAccessAction.GrantAccess:
            {
                var newlyGrantRollbacks = new List<DragBridgeGrantRollbackEntry>();
                var newlyTraverseRollbacks = new List<DragBridgeTraverseRollbackEntry>();
                foreach (var path in inaccessibleByTarget)
                {
                    try
                    {
                        ApplyFileGrant(
                            path,
                            targetSid.Value,
                            targetSids,
                            grantRollbacks,
                            newlyGrantRollbacks,
                            traverseRollbacks,
                            newlyTraverseRollbacks,
                            grantedPaths,
                            warnings,
                            ref dbModified,
                            ref durableSaveCompleted);
                    }
                    catch (Exception ex)
                    {
                        log.Warn($"DragBridgePasteHandler: failed to grant access to '{path}': {ex.Message}");
                        RollBackGrantBatch(newlyGrantRollbacks, newlyTraverseRollbacks);
                        return new DragBridgeResolveResult(null, false, [], null);
                    }
                }

                break;
            }
            case DragBridgeAccessAction.GrantFolderAccess:
            {
                var sourceDirs = inaccessibleByTarget
                    .Select(path => Directory.Exists(path) ? path : Path.GetDirectoryName(path)!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var newlyGrantRollbacks = new List<DragBridgeGrantRollbackEntry>();
                var newlyTraverseRollbacks = new List<DragBridgeTraverseRollbackEntry>();
                foreach (var dir in sourceDirs)
                {
                    try
                    {
                        ApplyFolderGrant(
                            dir,
                            targetSid.Value,
                            GetRequiredFolderGrantSids(dir, inaccessibleByTarget, targetSids),
                            grantRollbacks,
                            newlyGrantRollbacks,
                            traverseRollbacks,
                            newlyTraverseRollbacks,
                            grantedPaths,
                            warnings,
                            ref dbModified,
                            ref durableSaveCompleted);
                    }
                    catch (Exception ex)
                    {
                        log.Warn($"DragBridgePasteHandler: failed to grant folder access to '{dir}': {ex.Message}");
                        RollBackGrantBatch(newlyGrantRollbacks, newlyTraverseRollbacks);
                        return new DragBridgeResolveResult(null, false, [], null);
                    }
                }

                break;
            }
            case DragBridgeAccessAction.CopyToTemp:
                try
                {
                    var tempFolderResult = tempManager.CreateTempFolder(targetSid.Value, targetContainerSid?.Value);
                    if (!tempFolderResult.Succeeded || string.IsNullOrWhiteSpace(tempFolderResult.TempFolderPath))
                    {
                        ShowTempCopyError(tempFolderResult.ErrorMessage);
                        return new DragBridgeResolveResult(null, false, [], null);
                    }

                    var tempResult = tempManager.CopyFilesToTemp(tempFolderResult.TempFolderPath, inaccessibleByTarget);
                    if (!tempResult.Succeeded)
                    {
                        foreach (var path in tempResult.TempPaths)
                        {
                            try
                            {
                                if (Directory.Exists(path))
                                    Directory.Delete(path, recursive: true);
                                else if (File.Exists(path))
                                    File.Delete(path);
                            }
                            catch (Exception ex)
                            {
                                log.Warn($"DragBridgePasteHandler: temp rollback failed for '{path}': {ex.Message}");
                            }
                        }

                        ShowTempCopyError(tempResult.Entries.FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.ErrorText))?.ErrorText);
                        return new DragBridgeResolveResult(null, false, [], null);
                    }

                    filePaths = filePaths.Except(inaccessibleByTarget).Concat(tempResult.TempPaths).ToList();
                    return new DragBridgeResolveResult(
                        filePaths,
                        dbModified,
                        grantedPaths,
                        new DragBridgeRollbackPlan(grantRollbacks, traverseRollbacks, tempResult.TempPaths))
                    {
                        DurableSaveCompleted = durableSaveCompleted,
                        Warnings = warnings
                    };
                }
                catch (Exception ex)
                {
                    log.Error("DragBridgePasteHandler: failed to create temp folder for paste", ex);
                    ShowTempCopyError(null);
                    return new DragBridgeResolveResult(null, false, [], null);
                }
        }

        var rollbackPlan = grantRollbacks.Count == 0 && traverseRollbacks.Count == 0
            ? null
            : new DragBridgeRollbackPlan(grantRollbacks, traverseRollbacks, []);
        return new DragBridgeResolveResult(filePaths, dbModified, grantedPaths, rollbackPlan)
        {
            DurableSaveCompleted = durableSaveCompleted,
            Warnings = warnings
        };
    }

    public bool NeedsAccessResolution(SecurityIdentifier targetSid, SecurityIdentifier? targetContainerSid, IReadOnlyList<string> filePaths)
    {
        var targetSids = GetEffectiveTargetSids(targetSid, targetContainerSid);
        return filePaths.Any(path =>
            (!File.Exists(path) && !Directory.Exists(path)) ||
            targetSids.Any(sid => aclPermission.NeedsPermissionGrant(path, sid, FileSystemRights.Read)));
    }

    private void ApplyFileGrant(
        string path,
        string primaryTargetSid,
        IReadOnlyList<string> targetSids,
        List<DragBridgeGrantRollbackEntry> grantRollbacks,
        List<DragBridgeGrantRollbackEntry> newlyGrantRollbacks,
        List<DragBridgeTraverseRollbackEntry> traverseRollbacks,
        List<DragBridgeTraverseRollbackEntry> newlyTraverseRollbacks,
        List<string> grantedPaths,
        List<string> warnings,
        ref bool dbModified,
        ref bool durableSaveCompleted)
    {
        var traversePath = Path.GetDirectoryName(Path.GetFullPath(path));
        foreach (var sid in targetSids.Where(targetSid => aclPermission.NeedsPermissionGrant(path, targetSid, FileSystemRights.Read)))
        {
            var previousGrantState = pathGrantService.CaptureGrantRestoreSnapshot(sid, path, isDeny: false);
            var previousTraverseState = string.IsNullOrEmpty(traversePath)
                ? new GrantIntentRestoreSnapshot(null, [])
                : pathGrantService.CaptureTraverseRestoreSnapshot(sid, traversePath);
            var result = pathGrantService.EnsureAccess(
                sid,
                path,
                FileSystemRights.Read | FileSystemRights.Synchronize,
                confirm: null);

            dbModified |= result.DatabaseModified;
            durableSaveCompleted &= !result.DatabaseModified || result.DurableSaveCompleted;
            if (result.DatabaseModified)
            {
                var rollback = new DragBridgeGrantRollbackEntry(sid, path, previousGrantState);
                grantRollbacks.Add(rollback);
                newlyGrantRollbacks.Add(rollback);

                if (!string.IsNullOrEmpty(traversePath))
                {
                    var traverseRollback = new DragBridgeTraverseRollbackEntry(sid, traversePath, previousTraverseState);
                    traverseRollbacks.Add(traverseRollback);
                    newlyTraverseRollbacks.Add(traverseRollback);
                }
            }

            if (result.GrantApplied && string.Equals(sid, primaryTargetSid, StringComparison.OrdinalIgnoreCase))
                grantedPaths.Add(path);

            if (result.Warnings.Count > 0)
                warnings.AddRange(result.Warnings.Select(GrantApplyFailureFormatter.Format));
        }
    }

    private void ApplyFolderGrant(
        string dir,
        string primaryTargetSid,
        IReadOnlyList<string> targetSids,
        List<DragBridgeGrantRollbackEntry> grantRollbacks,
        List<DragBridgeGrantRollbackEntry> newlyGrantRollbacks,
        List<DragBridgeTraverseRollbackEntry> traverseRollbacks,
        List<DragBridgeTraverseRollbackEntry> newlyTraverseRollbacks,
        List<string> grantedPaths,
        List<string> warnings,
        ref bool dbModified,
        ref bool durableSaveCompleted)
    {
        var normalizedDir = Path.GetFullPath(dir);
        foreach (var sid in targetSids)
        {
            var previousGrantState = pathGrantService.CaptureGrantRestoreSnapshot(sid, normalizedDir, isDeny: false);
            var previousTraverseState = pathGrantService.CaptureTraverseRestoreSnapshot(sid, normalizedDir);
            var result = pathGrantService.EnsureAccess(
                sid,
                dir,
                FileSystemRights.ReadAndExecute,
                confirm: null);

            dbModified |= result.DatabaseModified;
            durableSaveCompleted &= !result.DatabaseModified || result.DurableSaveCompleted;
            if (result.DatabaseModified)
            {
                var rollback = new DragBridgeGrantRollbackEntry(sid, dir, previousGrantState);
                grantRollbacks.Add(rollback);
                newlyGrantRollbacks.Add(rollback);

                var traverseRollback = new DragBridgeTraverseRollbackEntry(sid, normalizedDir, previousTraverseState);
                traverseRollbacks.Add(traverseRollback);
                newlyTraverseRollbacks.Add(traverseRollback);
            }

            if (result.GrantApplied && string.Equals(sid, primaryTargetSid, StringComparison.OrdinalIgnoreCase))
                grantedPaths.Add(dir);

            if (result.Warnings.Count > 0)
                warnings.AddRange(result.Warnings.Select(GrantApplyFailureFormatter.Format));
        }
    }

    private void RollBackGrantBatch(
        IReadOnlyList<DragBridgeGrantRollbackEntry> grantRollbacks,
        IReadOnlyList<DragBridgeTraverseRollbackEntry> traverseRollbacks)
    {
        foreach (var rollback in grantRollbacks)
        {
            try
            {
                pathGrantService.RestoreGrant(
                    rollback.Sid,
                    rollback.Path,
                    isDeny: false,
                    rollback.PreviousState);
            }
            catch (Exception rollbackEx)
            {
                log.Warn($"DragBridgePasteHandler: rollback grant restore failed for '{rollback.Path}': {rollbackEx.Message}");
            }
        }

        foreach (var traverseRollback in traverseRollbacks)
        {
            try
            {
                pathGrantService.RestoreTraverse(
                    traverseRollback.Sid,
                    traverseRollback.Path,
                    traverseRollback.PreviousState);
            }
            catch (Exception rollbackEx)
            {
                log.Warn($"DragBridgePasteHandler: rollback traverse restore failed for '{traverseRollback.Path}': {rollbackEx.Message}");
            }
        }
    }

    private async Task<DragBridgeAccessAction?> AskUserAboutAccessAsync(
        SecurityIdentifier targetSid,
        SecurityIdentifier? targetContainerSid,
        List<string> inaccessiblePaths,
        AppDatabase? database,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<DragBridgeAccessAction?>();
        var targetDisplayName = ResolveTargetDisplayName(targetSid, targetContainerSid, database);
        var totalSize = inaccessiblePaths.Sum(path =>
        {
            try
            {
                if (File.Exists(path))
                    return new FileInfo(path).Length;
                if (Directory.Exists(path))
                    return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
                return 0L;
            }
            catch
            {
                return 0L;
            }
        });

        uiThreadInvoker.Invoke(() =>
        {
            try
            {
                var result = accessPrompt.Ask(targetDisplayName, inaccessiblePaths, totalSize);
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        try
        {
            return await tcs.Task.WaitAsync(ct);
        }
        catch
        {
            return null;
        }
    }

    private string ResolveTargetDisplayName(
        SecurityIdentifier targetSid,
        SecurityIdentifier? targetContainerSid,
        AppDatabase? database)
    {
        if (targetContainerSid != null && database != null)
        {
            var container = database.AppContainers.FirstOrDefault(entry =>
                string.Equals(entry.Sid, targetContainerSid.Value, StringComparison.OrdinalIgnoreCase));
            if (container != null)
                return string.IsNullOrWhiteSpace(container.DisplayName) ? container.Name : container.DisplayName;
        }

        return displayNameResolver.GetDisplayName(targetSid.Value, null, database?.SidNames);
    }

    private static string GetTargetIdentityKey(SecurityIdentifier targetSid, SecurityIdentifier? targetContainerSid)
        => targetContainerSid == null
            ? targetSid.Value
            : targetSid.Value + "|" + targetContainerSid.Value;

    private static List<string> GetEffectiveTargetSids(SecurityIdentifier targetSid, SecurityIdentifier? targetContainerSid)
    {
        var result = new List<string> { targetSid.Value };
        if (targetContainerSid != null)
            result.Add(targetContainerSid.Value);
        return result;
    }

    private static List<string> GetEffectiveSourceSids(string sourceSid, string? sourceContainerSid)
    {
        var result = new List<string> { sourceSid };
        if (!string.IsNullOrWhiteSpace(sourceContainerSid))
            result.Add(sourceContainerSid);
        return result;
    }

    private List<string> GetRequiredFolderGrantSids(string dir, IReadOnlyList<string> inaccessiblePaths, IReadOnlyList<string> targetSids)
    {
        var normalizedDir = Path.GetFullPath(dir);
        var pathsInDir = inaccessiblePaths
            .Where(path =>
                string.Equals(
                    Directory.Exists(path) ? Path.GetFullPath(path) : Path.GetDirectoryName(path),
                    normalizedDir,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        return targetSids
            .Where(sid => pathsInDir.Any(path => aclPermission.NeedsPermissionGrant(path, sid, FileSystemRights.Read)))
            .ToList();
    }

    private void ShowTempCopyError(string? detail)
    {
        var message = string.IsNullOrWhiteSpace(detail)
            ? "Failed to copy files to temp folder."
            : $"Failed to copy files to temp folder. {detail}";
        uiThreadInvoker.Invoke(() => notifications.ShowError("Drag Bridge", message));
    }
}
