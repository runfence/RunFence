using System.Security.AccessControl;
using System.Security.Principal;

namespace RunFence.Persistence;

public class PersistenceAtomicFileWriter(IPersistenceFileSecurityMirror persistenceFileSecurityMirror)
    : IPersistenceAtomicFileWriter
{
    public void AtomicWrite(string targetPath, byte[] data)
        => AtomicWrite(targetPath, data, finalSecurity: null);

    public void AtomicWrite(string targetPath, byte[] data, FileSecurity? finalSecurity)
    {
        EnsureDirectory(targetPath);
        var dir = Path.GetDirectoryName(targetPath)!;
        var tmpPath = Path.Combine(dir, $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        var bakPath = GetRollbackBackupPath(targetPath);

        try
        {
            WriteTemporaryFile(tmpPath, data, finalSecurity == null ? null : CreateTemporaryFileSecurity());

            var replacedExistingTarget = File.Exists(targetPath);
            if (replacedExistingTarget)
            {
                File.Replace(tmpPath, targetPath, bakPath);
            }
            else
            {
                File.Move(tmpPath, targetPath);
            }

            try
            {
                if (finalSecurity != null)
                    persistenceFileSecurityMirror.ApplyFileSecurity(targetPath, finalSecurity);
            }
            catch
            {
                RestoreAfterSecurityApplyFailure(targetPath, bakPath, replacedExistingTarget);
                throw;
            }

            if (replacedExistingTarget)
                TryDelete(bakPath);
        }
        catch
        {
            TryDelete(tmpPath);
            throw;
        }
    }

    public void AtomicCopy(string sourcePath, string targetPath, FileSecurity? finalSecurity)
    {
        EnsureDirectory(targetPath);
        var dir = Path.GetDirectoryName(targetPath)!;
        var tmpPath = Path.Combine(dir, $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        var bakPath = GetRollbackBackupPath(targetPath);

        try
        {
            CopyTemporaryFile(sourcePath, tmpPath, finalSecurity == null ? null : CreateTemporaryFileSecurity());

            var replacedExistingTarget = File.Exists(targetPath);
            if (replacedExistingTarget)
            {
                File.Replace(tmpPath, targetPath, bakPath);
            }
            else
            {
                File.Move(tmpPath, targetPath);
            }

            try
            {
                if (finalSecurity != null)
                    persistenceFileSecurityMirror.ApplyFileSecurity(targetPath, finalSecurity);
            }
            catch
            {
                RestoreAfterSecurityApplyFailure(targetPath, bakPath, replacedExistingTarget);
                throw;
            }

            if (replacedExistingTarget)
                TryDelete(bakPath);
        }
        catch
        {
            TryDelete(tmpPath);
            throw;
        }
    }

    /// <summary>
    /// Two-phase atomic write with rollback.
    /// Phase 1: write all temporary files.
    /// Phase 2: replace all targets in the provided order.
    /// Rollback: on Phase 2 failure, restore already-replaced files from rollback backups
    /// and delete any newly-created targets.
    /// </summary>
    public void AtomicWriteBatch(IReadOnlyList<(string path, byte[] data)> files)
    {
        var tmpFiles = new List<(string tmpPath, string targetPath)>();
        var replacedFiles = new List<(string targetPath, string bakPath)>();
        var createdFiles = new List<string>();
        try
        {
            foreach (var (path, data) in files)
            {
                EnsureDirectory(path);
                var dir = Path.GetDirectoryName(path)!;
                var tmpPath = Path.Combine(dir, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
                File.WriteAllBytes(tmpPath, data);
                tmpFiles.Add((tmpPath, path));
            }

            foreach (var (tmpPath, targetPath) in tmpFiles)
            {
                var bakPath = GetRollbackBackupPath(targetPath);
                if (File.Exists(targetPath))
                {
                    File.Replace(tmpPath, targetPath, bakPath);
                    replacedFiles.Add((targetPath, bakPath));
                }
                else
                {
                    File.Move(tmpPath, targetPath);
                    createdFiles.Add(targetPath);
                }
            }

            foreach (var (_, bakPath) in replacedFiles)
            {
                try
                {
                    File.Delete(bakPath);
                }
                catch
                {
                }
            }
        }
        catch
        {
            foreach (var (targetPath, bakPath) in replacedFiles)
            {
                try
                {
                    if (File.Exists(bakPath))
                        File.Move(bakPath, targetPath, overwrite: true);
                }
                catch
                {
                }
            }

            foreach (var createdPath in createdFiles)
            {
                try
                {
                    File.Delete(createdPath);
                }
                catch
                {
                }
            }

            foreach (var (tmpPath, _) in tmpFiles)
            {
                try
                {
                    if (File.Exists(tmpPath))
                        File.Delete(tmpPath);
                }
                catch
                {
                }
            }

            throw;
        }
    }

    public string GetRollbackBackupPath(string targetPath) =>
        $"{targetPath}.{Guid.NewGuid():N}.rollback";

    public void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private static void WriteTemporaryFile(string tmpPath, byte[] data, FileSecurity? creationSecurity)
    {
        if (creationSecurity == null)
        {
            File.WriteAllBytes(tmpPath, data);
            return;
        }

        using var stream = FileSystemAclExtensions.Create(
            new FileInfo(tmpPath),
            FileMode.CreateNew,
            FileSystemRights.WriteData,
            FileShare.None,
            4096,
            FileOptions.None,
            creationSecurity);
        stream.Write(data, 0, data.Length);
    }

    private static void CopyTemporaryFile(string sourcePath, string tmpPath, FileSecurity? creationSecurity)
    {
        if (creationSecurity == null)
        {
            File.Copy(sourcePath, tmpPath, overwrite: false);
            return;
        }

        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var destination = FileSystemAclExtensions.Create(
            new FileInfo(tmpPath),
            FileMode.CreateNew,
            FileSystemRights.WriteData,
            FileShare.None,
            4096,
            FileOptions.None,
            creationSecurity);
        source.CopyTo(destination);
    }

    private static FileSecurity CreateTemporaryFileSecurity()
    {
        var currentUserSid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current Windows identity does not have a SID.");
        var administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUserSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            administratorsSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        return security;
    }

    private static void RestoreAfterSecurityApplyFailure(
        string targetPath,
        string bakPath,
        bool replacedExistingTarget)
    {
        try
        {
            if (replacedExistingTarget)
            {
                if (File.Exists(bakPath))
                    File.Move(bakPath, targetPath, overwrite: true);
            }
            else if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
        }
        finally
        {
            if (replacedExistingTarget)
                TryDelete(bakPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
