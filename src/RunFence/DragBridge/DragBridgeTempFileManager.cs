using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core;

namespace RunFence.DragBridge;

public class DragBridgeTempFileManager : IDragBridgeTempFileManager
{
    private readonly ILoggingService _log;
    private readonly IAclPermissionService _aclPermission;
    private readonly AncestorTraverseGranter _traverseGranter;

    /// <inheritdoc/>
    public event Action<string, List<string>>? TraverseGranted;

    private string TempRoot => field
                               ?? Path.Combine(Constants.ProgramDataDir, Constants.DragBridgeTempDir);

    public DragBridgeTempFileManager(
        ILoggingService log,
        IAclPermissionService aclPermission,
        AncestorTraverseGranter traverseGranter,
        string? basePath = null)
    {
        _log = log;
        _aclPermission = aclPermission;
        _traverseGranter = traverseGranter;
        TempRoot = basePath;
    }

    public string CreateTempFolder(string targetSid, string? containerSid = null)
    {
        Directory.CreateDirectory(TempRoot);

        // Ensure each involved SID can traverse TempRoot and all ancestors to reach their
        // per-session subfolder. This is needed for accounts not in BUILTIN\Users
        // (e.g. AppContainer package SIDs) that wouldn't have traverse rights from default
        // ProgramData ACL inheritance. Full ancestor walk covers paths with broken inheritance.
        EnsureTraverseAccess(TempRoot, targetSid);
        if (containerSid != null)
            EnsureTraverseAccess(TempRoot, containerSid);

        var tempFolder = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);

        var targetIdentity = new SecurityIdentifier(targetSid);
        if (containerSid != null)
        {
            var containerIdentity = new SecurityIdentifier(containerSid);
            TempDirectoryAclHelper.ApplyRestrictedAcl(new DirectoryInfo(tempFolder),
                (targetIdentity, FileSystemRights.ReadAndExecute),
                (containerIdentity, FileSystemRights.ReadAndExecute));
        }
        else
        {
            TempDirectoryAclHelper.ApplyRestrictedAcl(new DirectoryInfo(tempFolder),
                (targetIdentity, FileSystemRights.ReadAndExecute));
        }

        return tempFolder;
    }

    private void EnsureTraverseAccess(string dirPath, string sid)
    {
        try
        {
            var identity = new SecurityIdentifier(sid);
            var groupSids = _aclPermission.ResolveAccountGroupSids(sid);
            var (appliedPaths, anyAceAdded) = _traverseGranter.GrantOnPathAndAncestors(dirPath, identity, groupSids: groupSids);
            if (anyAceAdded)
                TraverseGranted?.Invoke(sid, appliedPaths);
        }
        catch (Exception ex)
        {
            _log.Warn($"DragBridgeTempFileManager: traverse grant failed for '{sid}' on '{dirPath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Copies files and directories into the temp folder. Returns the actual destination paths
    /// for each successfully copied item (collision-renamed paths are returned as-is).
    /// Directories are copied recursively.
    /// </summary>
    public List<string> CopyFilesToTemp(string tempFolder, IReadOnlyList<string> filePaths)
    {
        var result = new List<string>(filePaths.Count);
        foreach (var src in filePaths)
        {
            try
            {
                if (Directory.Exists(src))
                {
                    var dirName = Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    var destDir = ResolveCollisionFreeDestination(tempFolder, dirName, isDirectory: true);
                    CopyDirectoryRecursive(src, destDir);
                    result.Add(destDir);
                }
                else if (File.Exists(src))
                {
                    var dest = ResolveCollisionFreeDestination(tempFolder, Path.GetFileName(src), isDirectory: false);
                    File.Copy(src, dest);
                    result.Add(dest);
                }
                else
                {
                    _log.Warn($"DragBridgeTempFileManager: source path no longer exists, skipping: '{src}'");
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"DragBridgeTempFileManager: failed to copy '{src}': {ex.Message}");
            }
        }

        return result;
    }

    private static string ResolveCollisionFreeDestination(string parentFolder, string name, bool isDirectory)
    {
        var dest = Path.Combine(parentFolder, name);
        if (!isDirectory)
        {
            if (!File.Exists(dest))
                return dest;
            var baseName = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name);
            int i = 1;
            do
            {
                dest = Path.Combine(parentFolder, $"{baseName}_{i++}{ext}");
            } while (File.Exists(dest));
        }
        else
        {
            if (!Directory.Exists(dest))
                return dest;
            int i = 1;
            do
            {
                dest = Path.Combine(parentFolder, $"{name}_{i++}");
            } while (Directory.Exists(dest));
        }

        return dest;
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                continue;
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            if ((new DirectoryInfo(subDir).Attributes & FileAttributes.ReparsePoint) != 0)
                continue;
            CopyDirectoryRecursive(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
        }
    }

    public void CleanupOldFolders(TimeSpan maxAge)
    {
        try
        {
            var root = TempRoot;
            if (!Directory.Exists(root))
                return;
            foreach (var dir in Directory.GetDirectories(root))
            {
                try
                {
                    var info = new DirectoryInfo(dir);
                    if (DateTime.UtcNow - info.CreationTimeUtc > maxAge)
                        Directory.Delete(dir, true);
                }
                catch
                {
                } // best-effort per-folder
            }
        }
        catch
        {
        } // best-effort overall
    }
}