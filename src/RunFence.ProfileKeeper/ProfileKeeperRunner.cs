using RunFence.Launching.Processes;
using RunFence.Core;

namespace RunFence.ProfileKeeper;

public sealed class ProfileKeeperRunner(
    ProfileKeeperIdentity identity,
    ProfileKeeperOptions options,
    IProcessSnapshotReader processSnapshotReader,
    ProfileKeeperStateEvaluator stateEvaluator,
    IProfileKeeperProcessTerminator processTerminator,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan CleanupWaitBudget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CleanupRecheckInterval = TimeSpan.FromSeconds(1);

    public void Run(CancellationToken cancellationToken)
    {
        DateTimeOffset? idleSinceUtc = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var scanResult = stateEvaluator.Evaluate(identity, processSnapshotReader.GetProcesses());
            if (!scanResult.IsNewestKeeper)
                scanResult = stateEvaluator.Evaluate(identity, processSnapshotReader.GetProcesses());
            if (!scanResult.IsNewestKeeper)
                return;

            var nowUtc = timeProvider.GetUtcNow();
            if (scanResult.SameSidBlockingProcessCount > 0)
            {
                idleSinceUtc = null;
            }
            else
            {
                idleSinceUtc ??= nowUtc;
                if (nowUtc - idleSinceUtc.Value >= options.IdleGracePeriod)
                {
                    var cleanupResult = KeeperExitHelper.TryFinalizeExit(
                        new KeeperExitScanResult<int>(
                            scanResult.SameSidBlockingProcessCount,
                            scanResult.IgnorableProcessIds),
                        processTerminator.Terminate,
                        maxCleanupPasses: GetCleanupPassCount(),
                        () => !cancellationToken.WaitHandle.WaitOne(CleanupRecheckInterval),
                        () =>
                        {
                            var finalScan = stateEvaluator.Evaluate(identity, processSnapshotReader.GetProcesses());
                            return new KeeperExitScanResult<int>(
                                finalScan.SameSidBlockingProcessCount,
                                finalScan.IgnorableProcessIds);
                        });
                    if (cleanupResult.CanExit)
                        return;

                    idleSinceUtc = null;
                }
            }

            if (cancellationToken.WaitHandle.WaitOne(options.ScanInterval))
                return;
        }
    }

    private static int GetCleanupPassCount() =>
        Math.Max(1, (int)Math.Ceiling(CleanupWaitBudget.TotalMilliseconds / CleanupRecheckInterval.TotalMilliseconds));
}
