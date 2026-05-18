using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.DragBridge;

public class DragBridgeTempFileManager(
    ILoggingService log,
    IPathGrantService pathGrantService,
    ITempDirectoryAclHelper aclHelper,
    string tempRoot)
    : IDragBridgeTempFileManager
{
    private string TempRoot { get; } = tempRoot;

    public DragBridgeTempFolderResult CreateTempFolder(string targetSid, string? containerSid = null)
    {
        string? tempFolder = null;
        try
        {
            Directory.CreateDirectory(TempRoot);

            // Ensure each involved SID can traverse TempRoot and all ancestors to reach their
            // per-session subfolder. This is needed for accounts not in BUILTIN\Users
            // (e.g. AppContainer package SIDs) that wouldn't have traverse rights from default
            // ProgramData ACL inheritance. Full ancestor walk covers paths with broken inheritance.
            EnsureTraverseAccess(TempRoot, targetSid);
            if (containerSid != null)
                EnsureTraverseAccess(TempRoot, containerSid);

            tempFolder = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempFolder);

            var targetIdentity = new SecurityIdentifier(targetSid);
            if (containerSid != null)
            {
                var containerIdentity = new SecurityIdentifier(containerSid);
                aclHelper.ApplyRestrictedAcl(new DirectoryInfo(tempFolder),
                    (targetIdentity, FileSystemRights.ReadAndExecute),
                    (containerIdentity, FileSystemRights.ReadAndExecute));
            }
            else
            {
                aclHelper.ApplyRestrictedAcl(new DirectoryInfo(tempFolder),
                    (targetIdentity, FileSystemRights.ReadAndExecute));
            }

            return new DragBridgeTempFolderResult(true, tempFolder, null);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrEmpty(tempFolder))
                TryDeleteTempFolder(tempFolder);

            log.Warn($"DragBridgeTempFileManager: failed to create temp folder: {ex.Message}");
            return new DragBridgeTempFolderResult(false, null, ex.Message);
        }
    }

    private void EnsureTraverseAccess(string dirPath, string sid)
    {
        pathGrantService.AddTraverse(sid, dirPath);
    }

    /// <summary>
    /// Copies files and directories into the temp folder. Returns the actual destination paths
    /// for each successfully copied item (collision-renamed paths are returned as-is).
    /// Directories are copied recursively.
    /// </summary>
    public DragBridgeTempFileResult CopyFilesToTemp(string tempFolder, IReadOnlyList<string> filePaths)
    {
        var tempPaths = new List<string>(filePaths.Count);
        var entries = new List<DragBridgeTempFileEntryResult>(filePaths.Count);
        foreach (var src in filePaths)
        {
            try
            {
                if (Directory.Exists(src))
                {
                    var dirName = Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    var destDir = ResolveCollisionFreeDestination(tempFolder, dirName, isDirectory: true);
                    CopyDirectoryRecursive(src, destDir);
                    tempPaths.Add(destDir);
                    entries.Add(new DragBridgeTempFileEntryResult(src, destDir,
                        DragBridgeTempFileCopyStatus.Succeeded, DragBridgeTempFileGrantStatus.NotAttempted,
                        DragBridgeTempFileRollbackStatus.NotRequired, null));
                }
                else if (File.Exists(src))
                {
                    var dest = ResolveCollisionFreeDestination(tempFolder, Path.GetFileName(src), isDirectory: false);
                    File.Copy(src, dest);
                    tempPaths.Add(dest);
                    entries.Add(new DragBridgeTempFileEntryResult(src, dest,
                        DragBridgeTempFileCopyStatus.Succeeded, DragBridgeTempFileGrantStatus.NotAttempted,
                        DragBridgeTempFileRollbackStatus.NotRequired, null));
                }
                else
                {
                    log.Warn($"DragBridgeTempFileManager: source path no longer exists, skipping: '{src}'");
                    entries.Add(new DragBridgeTempFileEntryResult(src, null,
                        DragBridgeTempFileCopyStatus.SourceMissing, DragBridgeTempFileGrantStatus.NotAttempted,
                        DragBridgeTempFileRollbackStatus.NotRequired, "Source path missing."));
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                log.Warn($"DragBridgeTempFileManager: failed to copy '{src}': {ex.Message}");
                entries.Add(new DragBridgeTempFileEntryResult(src, null,
                    DragBridgeTempFileCopyStatus.AccessDenied, DragBridgeTempFileGrantStatus.NotAttempted,
                    DragBridgeTempFileRollbackStatus.NotRequired, ex.Message));
            }
            catch (Exception ex)
            {
                log.Warn($"DragBridgeTempFileManager: failed to copy '{src}': {ex.Message}");
                entries.Add(new DragBridgeTempFileEntryResult(src, null,
                    DragBridgeTempFileCopyStatus.Failed, DragBridgeTempFileGrantStatus.NotAttempted,
                    DragBridgeTempFileRollbackStatus.NotRequired, ex.Message));
            }
        }

        var succeeded = entries.All(e => e.CopyStatus == DragBridgeTempFileCopyStatus.Succeeded);
        if (!succeeded)
        {
            TryDeleteTempFolder(tempFolder);
            tempPaths.Clear();
        }

        return new DragBridgeTempFileResult(succeeded, entries, tempPaths);
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

    private void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            {
                log.Warn($"DragBridgeTempFileManager: skipping reparse point (symlink/junction): '{file}'");
                continue;
            }
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            if ((new DirectoryInfo(subDir).Attributes & FileAttributes.ReparsePoint) != 0)
            {
                log.Warn($"DragBridgeTempFileManager: skipping reparse point (symlink/junction): '{subDir}'");
                continue;
            }
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

    private static void TryDeleteTempFolder(string tempFolder)
    {
        try
        {
            if (Directory.Exists(tempFolder))
                Directory.Delete(tempFolder, recursive: true);
        }
        catch
        {
        }
    }
}
