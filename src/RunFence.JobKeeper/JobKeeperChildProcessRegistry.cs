using RunFence.Core;

namespace RunFence.JobKeeper;

internal sealed class JobKeeperChildProcessRegistry(IJobKeeperNativeProcessApi nativeProcessApi)
    : IJobKeeperChildProcessRegistry
{
    private const uint ExitWaitMilliseconds = 5_000;

    private readonly List<TrackedProcessHandle> _processHandles = [];
    private readonly object _lock = new();

    public void Register(IntPtr processHandle)
    {
        if (processHandle == IntPtr.Zero)
            throw new ArgumentException("Process handle must be nonzero.", nameof(processHandle));

        lock (_lock)
        {
            PruneExitedProcessHandles();
            _processHandles.Add(new TrackedProcessHandle(
                processHandle,
                nativeProcessApi.TryGetProcessImagePath(processHandle)));
        }
    }

    public int PruneExitedAndCountActive()
    {
        lock (_lock)
        {
            PruneExitedProcessHandles();
            return BuildExitScan().BlockingProcessCount;
        }
    }

    public bool TryExitAfterCleaningIgnoredProcesses()
    {
        lock (_lock)
        {
            PruneExitedProcessHandles();
            var exitScan = BuildExitScan();
            var cleanupResult = KeeperExitHelper.TryFinalizeExit(
                exitScan,
                TerminateTrackedProcess,
                maxCleanupPasses: 1,
                () => true,
                () =>
                {
                    PruneExitedProcessHandles();
                    return BuildExitScan();
                });
            return cleanupResult.CanExit;
        }
    }

    private KeeperExitScanResult<IntPtr> BuildExitScan() =>
        KeeperExitHelper.Evaluate(_processHandles.Select(process =>
            new KeeperExitProcess<IntPtr>(
                process.Handle,
                IsKeeperProcess: false,
                process.ImagePath)));

    private void TerminateTrackedProcess(IntPtr processHandle)
    {
        nativeProcessApi.TerminateProcess(processHandle, 0);
        nativeProcessApi.WaitForProcessExit(processHandle, ExitWaitMilliseconds);
    }

    private void PruneExitedProcessHandles()
    {
        for (var i = _processHandles.Count - 1; i >= 0; i--)
        {
            var process = _processHandles[i];
            if (!nativeProcessApi.WaitForProcessExit(process.Handle, 0))
                continue;

            nativeProcessApi.CloseHandle(process.Handle);
            _processHandles.RemoveAt(i);
        }
    }

    private sealed record TrackedProcessHandle(IntPtr Handle, string? ImagePath);
}
