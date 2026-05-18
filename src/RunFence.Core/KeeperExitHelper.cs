namespace RunFence.Core;

public sealed record KeeperExitProcess<TProcessId>(TProcessId ProcessId, bool IsKeeperProcess, string? ImagePath);

public sealed record KeeperExitScanResult<TProcessId>(
    int BlockingProcessCount,
    IReadOnlyList<TProcessId> IgnorableProcessIds)
{
    public bool HasBlockingProcesses => BlockingProcessCount > 0;
}

public sealed record KeeperExitCleanupResult(bool CanExit, bool HasBlockingProcessesAfterCleanup);

public static class KeeperExitHelper
{
    public static KeeperExitScanResult<TProcessId> Evaluate<TProcessId>(
        IEnumerable<KeeperExitProcess<TProcessId>> processes)
    {
        var ignorableProcessIds = new List<TProcessId>();
        var blockingProcessCount = 0;
        foreach (var process in processes)
        {
            if (process.IsKeeperProcess)
                continue;

            if (KeeperCleanupIgnoredProcessHelper.IsIgnoredProcess(process.ImagePath))
            {
                ignorableProcessIds.Add(process.ProcessId);
                continue;
            }

            blockingProcessCount++;
        }

        return new KeeperExitScanResult<TProcessId>(blockingProcessCount, ignorableProcessIds);
    }

    public static KeeperExitCleanupResult TryFinalizeExit<TProcessId>(
        KeeperExitScanResult<TProcessId> initialScan,
        Action<TProcessId> terminateIgnorableProcess,
        int maxCleanupPasses,
        Func<bool> waitForCleanup,
        Func<KeeperExitScanResult<TProcessId>> rescan)
        where TProcessId : notnull
    {
        if (initialScan.HasBlockingProcesses)
            return new KeeperExitCleanupResult(false, true);

        var terminated = new HashSet<TProcessId>();
        var currentScan = initialScan;
        var cleanupPasses = 0;
        while (true)
        {
            foreach (var processId in currentScan.IgnorableProcessIds)
            {
                if (terminated.Add(processId))
                    terminateIgnorableProcess(processId);
            }

            if (currentScan.IgnorableProcessIds.Count == 0)
                return new KeeperExitCleanupResult(true, false);

            if (cleanupPasses >= maxCleanupPasses)
                return new KeeperExitCleanupResult(false, false);

            if (!waitForCleanup())
                return new KeeperExitCleanupResult(false, false);

            cleanupPasses++;
            currentScan = rescan();
            if (currentScan.HasBlockingProcesses)
                return new KeeperExitCleanupResult(false, true);
        }
    }
}
