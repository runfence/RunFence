using RunFence.Core;

namespace RunFence.Acl;

/// <summary>
/// Thin file system enumerator that calls <see cref="IPathGrantService.UpdateFromPath"/>
/// for each discovered path, delegating all ACL reading and DB classification to the service.
/// </summary>
public class AclManagerScanService(IPathGrantService pathGrantService, ILoggingService log) : IAclManagerScanService
{
    /// <summary>
    /// Enumerates all entries under <paramref name="rootPath"/> recursively and all ancestor
    /// directories up to the drive root, calling <see cref="IPathGrantService.UpdateFromPath"/>
    /// for each path. Returns the count of DB entries added or updated.
    /// </summary>
    public async Task<int> ScanAsync(
        string rootPath,
        string sid,
        IProgress<long> progress,
        CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            int updated = 0;
            long scanned = 0;

            var queue = new Queue<string>();
            queue.Enqueue(rootPath);

            while (queue.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var dir = queue.Dequeue();

                try
                {
                    if (pathGrantService.UpdateFromPath(dir, sid))
                        updated++;
                    scanned++;
                    progress.Report(scanned);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    log.Warn($"Scan: failed to process '{dir}': {ex.Message}");
                }

                try
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            // Skip junctions/symlinks to avoid infinite loops.
                            var attrs = File.GetAttributes(entry);
                            bool isReparsePoint = (attrs & FileAttributes.ReparsePoint) != 0;
                            if (isReparsePoint)
                                continue;

                            if ((attrs & FileAttributes.Directory) != 0)
                            {
                                queue.Enqueue(entry);
                            }
                            else
                            {
                                if (pathGrantService.UpdateFromPath(entry, sid))
                                    updated++;
                                scanned++;
                                progress.Report(scanned);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            log.Warn($"Scan: failed to process '{entry}': {ex.Message}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    log.Warn($"Scan: failed to enumerate '{dir}': {ex.Message}");
                }
            }

            var ancestor = new DirectoryInfo(rootPath).Parent;
            while (ancestor != null)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (ancestor.Exists)
                    {
                        if (pathGrantService.UpdateFromPath(ancestor.FullName, sid))
                            updated++;
                        scanned++;
                        progress.Report(scanned);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    log.Warn($"Scan: failed to process ancestor '{ancestor.FullName}': {ex.Message}");
                }

                ancestor = ancestor.Parent;
            }

            return updated;
        }, ct);
    }
}
