using System.Security.Principal;
using RunFence.Acl.QuickAccess;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.DragBridge;

/// <summary>
/// Orchestrates the access-resolution phase of the DragBridge flow.
/// Determines whether files need access resolution and constructs the resolve delegate
/// that grants ACLs, saves the config, and pins newly accessible folders.
/// </summary>
public class DragBridgeResolveOrchestrator(
    DragBridgePasteHandler pasteHandler,
    ISessionSaver sessionSaver,
    IQuickAccessPinService quickAccessPinService,
    IUiThreadInvoker uiThreadInvoker)
{
    /// <summary>
    /// Returns true when the target SID requires access resolution for the captured files
    /// (i.e. at least one file is inaccessible by the target).
    /// </summary>
    public bool NeedsAccessResolution(SecurityIdentifier targetSid, IReadOnlyList<string> filePaths)
        => pasteHandler.NeedsAccessResolution(targetSid, filePaths);

    /// <summary>
    /// Creates the resolve delegate for use by the bridge window.
    /// When invoked, it resolves file access for <paramref name="ownerInfo"/>, then
    /// saves the config and pins any newly granted folders on the UI thread.
    /// Returns null paths to signal abort.
    /// </summary>
    public Func<CancellationToken, Task<List<string>?>> CreateResolveDelegate(
        WindowOwnerInfo ownerInfo,
        List<string> capturedFiles,
        string sourceSid,
        AppDatabase db)
    {
        return async resolveCt =>
        {
            var r = await pasteHandler.ResolveFileAccessAsync(
                ownerInfo.Sid, capturedFiles, sourceSid, db.SidNames, resolveCt);
            if (r.DatabaseModified)
                uiThreadInvoker.Invoke(() =>
                {
                    sessionSaver.SaveConfig();
                    if (r.GrantedPaths.Count > 0)
                        quickAccessPinService.PinFolders(ownerInfo.Sid.Value, r.GrantedPaths);
                });
            return r.Paths;
        };
    }
}
