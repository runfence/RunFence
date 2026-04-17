using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.DragBridge;

/// <summary>
/// Result of <see cref="DragBridgePasteHandler.ResolveFileAccessAsync"/>. Null <see cref="Paths"/> means abort.
/// </summary>
/// <param name="Paths">Final file paths to deliver; null if the operation was aborted.</param>
/// <param name="DatabaseModified">True if any grant was tracked — caller must save config.</param>
/// <param name="GrantedPaths">Paths where ACEs were applied for the target account (for quick-access pinning).</param>
public readonly record struct DragBridgeResolveResult(
    List<string>? Paths,
    bool DatabaseModified,
    List<string> GrantedPaths);

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
    SidDisplayNameResolver displayNameResolver)
{
    // Key: targetSid + "|" + sorted inaccessible paths. Only CopyToTemp is remembered (grant choices are permanent
    // — access exists on next drag so the dialog never reappears).
    // Capacity-limited to 100 entries with oldest-first eviction to prevent unbounded memory growth.
    private const int RememberedChoicesMaxCapacity = 100;
    private readonly Dictionary<string, DragBridgeAccessAction> _rememberedChoices = new();
    private readonly Queue<string> _rememberedChoicesOrder = new();

    public async Task<DragBridgeResolveResult> ResolveFileAccessAsync(
        SecurityIdentifier targetSid,
        List<string> filePaths,
        string sourceSid,
        IReadOnlyDictionary<string, string>? sidNames,
        CancellationToken ct)
    {
        // Step 0: Verify files still exist
        var existingPaths = filePaths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (existingPaths.Count == 0)
        {
            uiThreadInvoker.Invoke(() => notifications.ShowWarning("Drag Bridge",
                $"{filePaths.Count - existingPaths.Count} source file(s) no longer exist."));
            return new DragBridgeResolveResult(null, false, []);
        }

        filePaths = existingPaths;

        // Step 1: Verify source user can read captured files
        var unreadableBySource = filePaths
            .Where(p => aclPermission.NeedsPermissionGrant(p, sourceSid, FileSystemRights.Read))
            .ToList();
        if (unreadableBySource.Count > 0)
        {
            uiThreadInvoker.Invoke(() => notifications.ShowWarning("Drag Bridge",
                $"Source account cannot read {unreadableBySource.Count} file(s). Cannot continue paste."));
            return new DragBridgeResolveResult(null, false, []);
        }

        // Step 2: Check if target user can access the files
        var inaccessibleByTarget = filePaths
            .Where(p => aclPermission.NeedsPermissionGrant(p, targetSid.Value, FileSystemRights.Read))
            .ToList();

        var grantedPaths = new List<string>();
        bool dbModified = false;
        if (inaccessibleByTarget.Count > 0)
        {
            var choiceKey = MakeChoiceKey(targetSid.Value, inaccessibleByTarget);
            DragBridgeAccessAction action;
            if (_rememberedChoices.TryGetValue(choiceKey, out var remembered))
            {
                action = remembered;
            }
            else
            {
                var chosen = await AskUserAboutAccessAsync(targetSid, inaccessibleByTarget, sidNames, ct);
                if (chosen == null)
                    return new DragBridgeResolveResult(null, false, []); // cancelled
                action = chosen.Value;
                if (action is DragBridgeAccessAction.CopyToTemp)
                {
                    if (_rememberedChoices.Count >= RememberedChoicesMaxCapacity)
                    {
                        var oldest = _rememberedChoicesOrder.Dequeue();
                        _rememberedChoices.Remove(oldest);
                    }

                    _rememberedChoicesOrder.Enqueue(choiceKey);
                    _rememberedChoices[choiceKey] = action;
                }
            }

            switch (action)
            {
                case DragBridgeAccessAction.GrantAccess:
                {
                    foreach (var path in inaccessibleByTarget)
                    {
                        try
                        {
                            // Grants Read + Synchronize on the specific file (not ReadAndExecute on parent directory).
                            // DragBridge only needs to read this exact file, not launch it.
                            // Outer GrantAccess action selection is the user approval — silent confirm here.
                            var r = pathGrantService.EnsureAccess(
                                targetSid.Value, path,
                                FileSystemRights.Read | FileSystemRights.Synchronize,
                                confirm: null);
                            dbModified |= r.DatabaseModified;
                            if (r.GrantAdded)
                                grantedPaths.Add(path);
                        }
                        catch (Exception ex)
                        {
                            log.Warn($"DragBridgePasteHandler: failed to grant access to '{path}': {ex.Message}");
                        }
                    }

                    break;
                }
                case DragBridgeAccessAction.GrantFolderAccess:
                {
                    // Grants ReadAndExecute on each unique parent folder (or the folder itself for directory paths),
                    // covering all contents via full inheritance. Returns original paths — no temp copies.
                    var sourceDirs = inaccessibleByTarget
                        .Select(p => Directory.Exists(p) ? p : Path.GetDirectoryName(p)!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    foreach (var dir in sourceDirs)
                    {
                        try
                        {
                            var r = pathGrantService.EnsureAccess(
                                targetSid.Value, dir, FileSystemRights.ReadAndExecute, confirm: null);
                            dbModified |= r.DatabaseModified;
                            if (r.GrantAdded)
                                grantedPaths.Add(dir);
                        }
                        catch (Exception ex)
                        {
                            log.Warn($"DragBridgePasteHandler: failed to grant folder access to '{dir}': {ex.Message}");
                        }
                    }

                    break;
                }
                case DragBridgeAccessAction.CopyToTemp:
                    try
                    {
                        // Container SID resolution is not feasible: all container apps share the same owner SID
                        // (the interactive user), so there is no reliable way to identify which container is
                        // the target from the window owner SID alone. Users can manually grant access via "Copy SID".
                        var tempFolder = tempManager.CreateTempFolder(targetSid.Value, containerSid: null);
                        var tempPaths = tempManager.CopyFilesToTemp(tempFolder, inaccessibleByTarget);

                        // Replace inaccessible paths with their actual temp copies (names may differ due to collision renaming)
                        filePaths = filePaths.Except(inaccessibleByTarget).Concat(tempPaths).ToList();
                    }
                    catch (Exception ex)
                    {
                        log.Error("DragBridgePasteHandler: failed to create temp folder for paste", ex);
                        uiThreadInvoker.Invoke(() => notifications.ShowError("Drag Bridge", "Failed to copy files to temp folder."));
                        return new DragBridgeResolveResult(null, false, []);
                    }

                    break;
            }
        }

        return new DragBridgeResolveResult(filePaths, dbModified, grantedPaths);
    }

    /// <summary>
    /// Returns true if any file is missing or inaccessible by the target account, indicating a
    /// resolution dialog will be needed. Used as a pre-check before opening the DragBridge window
    /// so that files already accessible start as resolved — no extra drag required.
    /// </summary>
    public bool NeedsAccessResolution(SecurityIdentifier targetSid, IReadOnlyList<string> filePaths)
        => filePaths.Any(p =>
            (!File.Exists(p) && !Directory.Exists(p)) ||
            aclPermission.NeedsPermissionGrant(p, targetSid.Value, FileSystemRights.Read));

    private static string MakeChoiceKey(string targetSid, IEnumerable<string> inaccessiblePaths)
        => targetSid + "|" + string.Join("|", inaccessiblePaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));

    private async Task<DragBridgeAccessAction?> AskUserAboutAccessAsync(
        SecurityIdentifier targetSid,
        List<string> inaccessiblePaths,
        IReadOnlyDictionary<string, string>? sidNames,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<DragBridgeAccessAction?>();

        var targetDisplayName = displayNameResolver.GetDisplayName(targetSid.Value, null, sidNames);
        var totalSize = inaccessiblePaths.Sum(p =>
        {
            try
            {
                if (File.Exists(p))
                    return new FileInfo(p).Length;
                if (Directory.Exists(p))
                    return new DirectoryInfo(p).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                return 0L;
            }
            catch (Exception)
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
        } // dialog error or cancellation → treat as cancel
    }
}