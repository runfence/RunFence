namespace RunFence.Acl;

public interface IAclManagerScanService
{
    /// <summary>
    /// Walks <paramref name="rootPath"/> and all ancestor directories, calling
    /// <see cref="IPathGrantService.UpdateFromPath"/> for each path. Returns the count of
    /// DB entries added or updated.
    /// </summary>
    Task<int> ScanAsync(
        string rootPath,
        string sid,
        IProgress<long> progress,
        CancellationToken ct);
}
