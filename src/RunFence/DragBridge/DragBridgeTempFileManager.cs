using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.DragBridge;

public class DragBridgeTempFileManager(
    ILoggingService log,
    ITraverseService traverseService,
    ITempDirectoryAclHelper aclHelper,
    IProgramDataDirectoryProvisioningService programDataDirectoryProvisioningService,
    IProgramDataManagedObjectRepairService programDataManagedObjectRepairService,
    IProgramDataPathPolicyService programDataPathPolicyService,
    IProgramDataObjectProvisioner programDataObjectProvisioner,
    IProgramDataKnownPathResolver programDataKnownPathResolver)
    : IDragBridgeTempFileManager
{
    private string TempRoot { get; } = programDataKnownPathResolver.GetDirectoryPath(
        ProgramDataPolicies.DragBridge);

    public DragBridgeTempFolderResult CreateTempFolder(string targetSid, string? containerSid = null)
    {
        string? tempFolder = null;
        try
        {
            EnsureTempRoot();

            // Ensure each involved SID can traverse TempRoot and all ancestors to reach their
            // per-session subfolder. This is needed for accounts not in BUILTIN\Users
            // (e.g. AppContainer package SIDs) that wouldn't have traverse rights from default
            // ProgramData ACL inheritance. Full ancestor walk covers paths with broken inheritance.
            EnsureTraverseAccess(TempRoot, targetSid);
            if (containerSid != null)
                EnsureTraverseAccess(TempRoot, containerSid);

            tempFolder = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
            var targetIdentity = new SecurityIdentifier(targetSid);
            if (programDataPathPolicyService.IsUnderRoot(tempFolder))
            {
                var additionalAccess = CreateTempFolderAccess(targetIdentity, containerSid);
                programDataObjectProvisioner.CreateOrRepairDirectory(
                    new ProgramDataExplicitDirectoryRequest(
                        tempFolder,
                        ProgramDataDirectoryAclProfile.CurrentProcessUserFullControl,
                        additionalAccess,
                        ReplaceExistingSecurity: true));
                LogProgramDataTempDirectoryCreation(tempFolder, targetSid, containerSid);
            }
            else if (containerSid != null)
            {
                Directory.CreateDirectory(tempFolder);
                var containerIdentity = new SecurityIdentifier(containerSid);
                var appliedSecurity = aclHelper.ApplyRestrictedAcl(new DirectoryInfo(tempFolder),
                    (targetIdentity, FileSystemRights.ReadAndExecute),
                    (containerIdentity, FileSystemRights.ReadAndExecute));
                LogProgramDataTempDirectoryCreation(tempFolder, appliedSecurity, targetSid, containerSid);
            }
            else
            {
                Directory.CreateDirectory(tempFolder);
                var appliedSecurity = aclHelper.ApplyRestrictedAcl(new DirectoryInfo(tempFolder),
                    (targetIdentity, FileSystemRights.ReadAndExecute));
                LogProgramDataTempDirectoryCreation(tempFolder, appliedSecurity, targetSid, containerSid: null);
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
        traverseService.AddTraverse(sid, dirPath);
    }

    private void EnsureTempRoot()
    {
        if (programDataPathPolicyService.IsUnderRoot(TempRoot))
        {
            var managedTempRoot = programDataDirectoryProvisioningService.EnsureKnownDirectory(
                ProgramDataPolicies.DragBridge);
            if (!string.Equals(
                    Path.GetFullPath(managedTempRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(TempRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"DragBridge temp root '{TempRoot}' does not match managed ProgramData DragBridge root '{managedTempRoot}'.");
            }

            return;
        }

        Directory.CreateDirectory(TempRoot);
    }

    /// <summary>
    /// Copies files and directories into the temp folder. Returns the actual destination paths
    /// for each successfully copied item (collision-renamed paths are returned as-is).
    /// Directories are copied recursively.
    /// </summary>
    public DragBridgeTempFileResult CopyFilesToTemp(string tempFolder, IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0)
            return new DragBridgeTempFileResult(true, [], []);

        var preflightFailure = TryEnsureWritableTempFolder(tempFolder, filePaths);
        if (preflightFailure != null)
            return preflightFailure;

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

    private DragBridgeTempFileResult? TryEnsureWritableTempFolder(string tempFolder, IReadOnlyList<string> filePaths)
    {
        if (!programDataPathPolicyService.IsUnderRoot(tempFolder))
            return null;

        try
        {
            EnsureTempRoot();
            programDataManagedObjectRepairService.EnsureManagedDirectoryOwner(tempFolder);
            return null;
        }
        catch (Exception ex)
        {
            log.Warn($"DragBridgeTempFileManager: temp folder security validation failed for '{tempFolder}': {ex.Message}");
            var entries = filePaths
                .Select(path => new DragBridgeTempFileEntryResult(
                    path,
                    null,
                    DragBridgeTempFileCopyStatus.Failed,
                    DragBridgeTempFileGrantStatus.NotAttempted,
                    DragBridgeTempFileRollbackStatus.NotRequired,
                    ex.Message))
                .ToList();
            return new DragBridgeTempFileResult(false, entries, []);
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

    private void LogProgramDataTempDirectoryCreation(
        string tempFolder,
        DirectorySecurity appliedSecurity,
        string targetSid,
        string? containerSid)
    {
        if (!programDataPathPolicyService.IsUnderRoot(tempFolder))
            return;

        log.Info(
            $"ProgramData security created restricted DragBridge temp directory '{tempFolder}' with "
            + $"{ProgramDataSecurityChangeFormatter.DescribeSecurityState(appliedSecurity)}; target SID '{targetSid}'"
            + (containerSid == null ? "." : $"; container SID '{containerSid}'."));
    }

    private void LogProgramDataTempDirectoryCreation(
        string tempFolder,
        string targetSid,
        string? containerSid)
        => log.Info(
            $"ProgramData security created restricted DragBridge temp directory '{tempFolder}' for target SID '{targetSid}'"
            + (containerSid == null ? "." : $"; container SID '{containerSid}'."));

    private static IReadOnlyList<ProgramDataPrincipalAccess> CreateTempFolderAccess(
        SecurityIdentifier targetIdentity,
        string? containerSid)
    {
        var access = new List<ProgramDataPrincipalAccess>
        {
            new(
                targetIdentity,
                FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None)
        };

        if (containerSid != null)
        {
            access.Add(new ProgramDataPrincipalAccess(
                new SecurityIdentifier(containerSid),
                FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None));
        }

        return access;
    }
}
