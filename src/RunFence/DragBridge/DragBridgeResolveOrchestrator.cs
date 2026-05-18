using System.Security.Principal;
using RunFence.Acl;
using RunFence.Acl.QuickAccess;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.DragBridge;

/// <summary>
/// Orchestrates the access-resolution phase of the DragBridge flow.
/// Determines whether files need access resolution and constructs the resolve delegate
/// that grants ACLs and pins newly accessible folders.
/// </summary>
public class DragBridgeResolveOrchestrator(
    IDragBridgePasteHandler pasteHandler,
    IQuickAccessPinService quickAccessPinService,
    IUiThreadInvoker uiThreadInvoker,
    INotificationService notifications,
    IPathGrantService pathGrantService)
{
    /// <summary>
    /// Returns true when the target SID requires access resolution for the captured files
    /// (i.e. at least one file is inaccessible by the target).
    /// </summary>
    public bool NeedsAccessResolution(SecurityIdentifier targetSid, SecurityIdentifier? targetContainerSid, IReadOnlyList<string> filePaths)
        => pasteHandler.NeedsAccessResolution(targetSid, targetContainerSid, filePaths);

    /// <summary>
    /// Creates the resolve delegate for use by the bridge window.
    /// When invoked, it resolves file access for <paramref name="ownerInfo"/>, then
    /// pins any newly granted folders on the UI thread.
    /// Returns null paths to signal abort.
    /// </summary>
    public Func<CancellationToken, Task<List<string>?>> CreateResolveDelegate(
        WindowOwnerInfo ownerInfo,
        List<string> capturedFiles,
        string sourceSid,
        string? sourceContainerSid,
        AppDatabase db)
    {
        return async resolveCt =>
        {
            var r = await pasteHandler.ResolveFileAccessAsync(
                ownerInfo.Sid, ownerInfo.AppContainerSid, capturedFiles, sourceSid, sourceContainerSid, db, resolveCt);
            if (r.DatabaseModified && !r.DurableSaveCompleted && r.Warnings.Count == 0)
            {
                if (r.RollbackPlan != null)
                {
                    foreach (var rollback in r.RollbackPlan.GrantRollbacks)
                    {
                        try
                        {
                            pathGrantService.RestoreGrant(
                                rollback.Sid,
                                rollback.Path,
                                isDeny: false,
                                rollback.PreviousState);
                        }
                        catch
                        {
                        }
                    }

                    foreach (var rollback in r.RollbackPlan.TraverseRollbacks)
                    {
                        try
                        {
                            pathGrantService.RestoreTraverse(
                                rollback.Sid,
                                rollback.Path,
                                rollback.PreviousState);
                        }
                        catch
                        {
                        }
                    }

                    foreach (var path in r.RollbackPlan.TempPaths)
                    {
                        try
                        {
                            if (Directory.Exists(path))
                                Directory.Delete(path, recursive: true);
                            else if (File.Exists(path))
                                File.Delete(path);
                        }
                        catch
                        {
                        }
                    }
                }

                throw new InvalidOperationException("Drag Bridge access resolution modified persisted grant state without a durable save.");
            }

            if (r.Warnings.Count > 0)
            {
                foreach (var warning in r.Warnings)
                    uiThreadInvoker.Invoke(() => notifications.ShowWarning("Drag Bridge", warning));
            }

            if (r.GrantedPaths.Count > 0)
            {
                try
                {
                    uiThreadInvoker.Invoke(() => quickAccessPinService.PinFolders(ownerInfo.Sid.Value, r.GrantedPaths));
                }
                catch
                {
                }
            }
            return r.Paths;
        };
    }
}
