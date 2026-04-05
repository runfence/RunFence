using System.Security.AccessControl;
using RunFence.Core;

namespace RunFence.Acl;

public readonly record struct AclTraversalEntry(string Path, bool IsDirectory, FileSystemSecurity Security);

public class FileSystemAclTraverser(ILoggingService log) : IFileSystemAclTraverser
{
    public IEnumerable<AclTraversalEntry> Traverse(
        IReadOnlyList<string> rootPaths,
        IProgress<long> progress,
        CancellationToken ct)
    {
        long scanned = 0;
        var stack = new Stack<string>();

        foreach (var root in rootPaths)
        {
            if (Directory.Exists(root))
                stack.Push(root);
        }

        while (stack.Count > 0)
        {
            if (scanned % 100 == 0)
                ct.ThrowIfCancellationRequested();

            var current = stack.Pop();

            FileSystemSecurity? dirSecurity = null;
            try
            {
                var dirInfo = new DirectoryInfo(current);
                dirSecurity = dirInfo.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            }
            catch (UnauthorizedAccessException)
            {
                log.Warn($"Access denied scanning: {current}");
            }
            catch (Exception ex)
            {
                log.Warn($"Error scanning {current}: {ex.Message}");
            }

            if (dirSecurity != null)
            {
                scanned++;
                if (scanned % 500 == 0)
                    progress.Report(scanned);
                yield return new AclTraversalEntry(current, true, dirSecurity);
            }

            string[]? files = null;
            try
            {
                files = Directory.GetFiles(current);
            }
            catch
            {
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
                        var fileInfo = new FileInfo(file);
                        fileSecurity = fileInfo.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
                    }
                    catch
                    {
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
                subdirs = Directory.GetDirectories(current);
            }
            catch
            {
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

                    stack.Push(sub);
                }
            }
        }

        progress.Report(scanned);
    }
}
