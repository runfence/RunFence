using System.Security.AccessControl;
using System.Security.Principal;
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
public class DragBridgePasteHandler
{
    private readonly IDragBridgeAccessPrompt _accessPrompt;
    private readonly IDragBridgeTempFileManager _tempManager;
    private readonly INotificationService _notifications;
    private readonly ILoggingService _log;
    private readonly IUiThreadInvoker _uiThreadInvoker;
    private readonly IAclPermissionService _aclPermission;
    private readonly IPermissionGrantService _permissionGrantService;
    private readonly SidDisplayNameResolver _displayNameResolver;

    // Key: targetSid + "|" + sorted inaccessible paths. Only CopyToTemp* choices are remembered.
    private readonly Dictionary<string, DragBridgeAccessAction> _rememberedChoices = new();

    public DragBridgePasteHandler(
        IDragBridgeAccessPrompt accessPrompt,
        IDragBridgeTempFileManager tempManager,
        INotificationService notifications,
        ILoggingService log,
        IUiThreadInvoker uiThreadInvoker,
        IAclPermissionService aclPermission,
        IPermissionGrantService permissionGrantService,
        SidDisplayNameResolver displayNameResolver)
    {
        _accessPrompt = accessPrompt;
        _tempManager = tempManager;
        _notifications = notifications;
        _log = log;
        _uiThreadInvoker = uiThreadInvoker;
        _aclPermission = aclPermission;
        _permissionGrantService = permissionGrantService;
        _displayNameResolver = displayNameResolver;
    }

    public async Task<DragBridgeResolveResult> ResolveFileAccessAsync(
        SecurityIdentifier targetSid,
        List<string> filePaths,
        string sourceSid,
        IReadOnlyDictionary<string, string>? sidNames,
        CancellationToken ct)
    {
        _tempManager.CleanupOldFolders(TimeSpan.FromHours(24));

        // Step 0: Verify files still exist
        var existingPaths = filePaths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (existingPaths.Count == 0)
        {
            _uiThreadInvoker.Invoke(() => _notifications.ShowWarning("Drag Bridge",
                $"{filePaths.Count - existingPaths.Count} source file(s) no longer exist."));
            return new DragBridgeResolveResult(null, false, []);
        }

        filePaths = existingPaths;

        // Step 1: Verify source user can read captured files
        var unreadableBySource = filePaths
            .Where(p => _aclPermission.NeedsPermissionGrant(p, sourceSid, FileSystemRights.Read))
            .ToList();
        if (unreadableBySource.Count > 0)
        {
            _uiThreadInvoker.Invoke(() => _notifications.ShowWarning("Drag Bridge",
                $"Source account cannot read {unreadableBySource.Count} file(s). Cannot continue paste."));
            return new DragBridgeResolveResult(null, false, []);
        }

        // Step 2: Check if target user can access the files
        var inaccessibleByTarget = filePaths
            .Where(p => _aclPermission.NeedsPermissionGrant(p, targetSid.Value, FileSystemRights.Read))
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
                if (action is DragBridgeAccessAction.CopyToTemp or DragBridgeAccessAction.CopyToTempWholeFolder)
                    _rememberedChoices[choiceKey] = action;
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
                            var r = _permissionGrantService.EnsureAccess(
                                path, targetSid.Value,
                                FileSystemRights.Read | FileSystemRights.Synchronize,
                                confirm: null);
                            dbModified |= r.DatabaseModified;
                            if (r.GrantAdded)
                                grantedPaths.Add(path);
                        }
                        catch (Exception ex)
                        {
                            _log.Warn($"DragBridgePasteHandler: failed to grant access to '{path}': {ex.Message}");
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
                        var tempFolder = _tempManager.CreateTempFolder(targetSid.Value, containerSid: null);
                        var tempPaths = _tempManager.CopyFilesToTemp(tempFolder, inaccessibleByTarget);

                        // Replace inaccessible paths with their actual temp copies (names may differ due to collision renaming)
                        filePaths = filePaths.Except(inaccessibleByTarget).Concat(tempPaths).ToList();
                    }
                    catch (Exception ex)
                    {
                        _log.Error("DragBridgePasteHandler: failed to create temp folder for paste", ex);
                        _uiThreadInvoker.Invoke(() => _notifications.ShowError("Drag Bridge", "Failed to copy files to temp folder."));
                        return new DragBridgeResolveResult(null, false, []);
                    }

                    break;
                // CopyToTempWholeFolder
                default:
                    try
                    {
                        var tempFolder = _tempManager.CreateTempFolder(targetSid.Value, containerSid: null);
                        filePaths = CopyWholeFoldersToTemp(tempFolder, filePaths, inaccessibleByTarget);
                    }
                    catch (Exception ex)
                    {
                        _log.Error("DragBridgePasteHandler: failed to create temp folder for whole-folder paste", ex);
                        _uiThreadInvoker.Invoke(() => _notifications.ShowError("Drag Bridge", "Failed to copy folders to temp folder."));
                        return new DragBridgeResolveResult(null, false, []);
                    }

                    break;
            }
        }

        return new DragBridgeResolveResult(filePaths, dbModified, grantedPaths);
    }

    private List<string> CopyWholeFoldersToTemp(string tempFolder, List<string> filePaths, List<string> inaccessibleByTarget)
    {
        // Determine the unique parent directories of inaccessible paths.
        // For inaccessible directories, use the directory itself; for files, use the parent.
        var sourceDirs = inaccessibleByTarget
            .Select(p => Directory.Exists(p) ? p : Path.GetDirectoryName(p)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dirMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var srcDir in sourceDirs)
        {
            var tempPaths = _tempManager.CopyFilesToTemp(tempFolder, [srcDir]);
            if (tempPaths.Count > 0)
                dirMapping[srcDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)] = tempPaths[0];
        }

        return filePaths.Select(p => RemapToTempDir(p, dirMapping)).ToList();
    }

    private static string RemapToTempDir(string path, Dictionary<string, string> dirMapping)
    {
        var normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var (srcDir, tempDir) in dirMapping)
        {
            if (normalized.Equals(srcDir, StringComparison.OrdinalIgnoreCase))
                return tempDir;
            if (normalized.StartsWith(srcDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(tempDir, normalized[(srcDir.Length + 1)..]);
        }

        return path; // already accessible
    }

    private static string MakeChoiceKey(string targetSid, IEnumerable<string> inaccessiblePaths)
        => targetSid + "|" + string.Join("|", inaccessiblePaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));

    private async Task<DragBridgeAccessAction?> AskUserAboutAccessAsync(
        SecurityIdentifier targetSid,
        List<string> inaccessiblePaths,
        IReadOnlyDictionary<string, string>? sidNames,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<DragBridgeAccessAction?>();

        var targetDisplayName = _displayNameResolver.GetDisplayName(targetSid.Value, null, sidNames);
        var totalSize = inaccessiblePaths
            .Where(File.Exists)
            .Sum(p =>
            {
                try
                {
                    return new FileInfo(p).Length;
                }
                catch
                {
                    return 0L;
                }
            });

        _uiThreadInvoker.Invoke(() =>
        {
            try
            {
                var result = _accessPrompt.Ask(targetDisplayName, inaccessiblePaths, totalSize);
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