using System.Security.AccessControl;
using RunFence.Core;

namespace RunFence.Acl;

public readonly record struct AclTraversalEntry(string Path, bool IsDirectory, FileSystemSecurity Security);

public class FileSystemAclTraverser(
    ILoggingService log,
    IPathSecurityDescriptorAccessor aclAccessor) : IFileSystemAclTraverser
{
    public IEnumerable<AclTraversalEntry> Traverse(
        IReadOnlyList<string> rootPaths,
        IProgress<long> progress,
        CancellationToken ct)
    {
        long scanned = 0;
        var stack = new Stack<(string Path, bool IsDirectory)>();

        foreach (var root in rootPaths)
        {
            if (aclAccessor.PathExists(root, out bool isDirectory))
                stack.Push((root, isDirectory));
        }

        while (stack.Count > 0)
        {
            if (scanned % 100 == 0)
                ct.ThrowIfCancellationRequested();

            var current = stack.Pop();
            if (!aclAccessor.PathExists(current.Path, out bool currentIsDirectory))
                continue;

            FileSystemSecurity? dirSecurity = null;
            try
            {
                dirSecurity = aclAccessor.GetSecurity(current.Path);
            }
            catch (UnauthorizedAccessException)
            {
                log.Warn($"Access denied scanning: {current.Path}");
            }
            catch (Exception ex)
            {
                log.Warn($"Error scanning {current.Path}: {ex.Message}");
            }

            if (dirSecurity != null)
            {
                scanned++;
                if (scanned % 500 == 0)
                    progress.Report(scanned);
                yield return new AclTraversalEntry(current.Path, currentIsDirectory, dirSecurity);
            }

            if (!currentIsDirectory)
                continue;

            string[]? files = null;
            try
            {
                files = Directory.GetFiles(current.Path);
            }
            catch (Exception ex)
            {
                log.Debug($"ACL access failed for '{current.Path}': {ex.Message}");
            }

            if (files != null)
            {
                foreach (var file in files)
                {
                    if (scanned % 100 == 0)
                        ct.ThrowIfCancellationRequested();

                    FileSystemSecurity? fileSecurity = null;
                    try
                    {
                        fileSecurity = aclAccessor.GetSecurity(file);
                    }
                    catch (Exception ex)
                    {
                        log.Debug($"ACL access failed for '{file}': {ex.Message}");
                    }

                    if (fileSecurity != null)
                    {
                        scanned++;
                        if (scanned % 500 == 0)
                            progress.Report(scanned);
                        yield return new AclTraversalEntry(file, false, fileSecurity);
                    }
                }
            }

            string[]? subdirs = null;
            try
            {
                subdirs = Directory.GetDirectories(current.Path);
            }
            catch (Exception ex)
            {
                log.Debug($"ACL access failed for '{current.Path}': {ex.Message}");
            }

            if (subdirs != null)
            {
                foreach (var sub in subdirs)
                {
                    // Skip reparse points (junctions, symlinks) to avoid infinite loops or
                    // traversing outside the intended scope. Inaccessible directories are also skipped.
                    try
                    {
                        if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
                            continue;
                    }
                    catch
                    {
                        continue;
                    }

                    stack.Push((sub, true));
                }
            }
        }

        progress.Report(scanned);
    }
}
