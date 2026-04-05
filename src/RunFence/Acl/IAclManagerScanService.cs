namespace RunFence.Acl;

public interface IAclManagerScanService
{
    /// <summary>
    /// Walks <paramref name="rootPath"/> and collects grant and traverse paths for the given SID.
    /// </summary>
    Task<ScanResult> ScanAsync(
        string rootPath,
        string sid,
        IProgress<long> progress,
        CancellationToken ct);
}