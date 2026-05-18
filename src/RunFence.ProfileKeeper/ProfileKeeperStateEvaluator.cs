using RunFence.Launching.Processes;
using RunFence.Core;

namespace RunFence.ProfileKeeper;

public sealed class ProfileKeeperStateEvaluator
{
    public ProfileKeeperScanResult Evaluate(
        ProfileKeeperIdentity identity,
        IReadOnlyCollection<ProcessSnapshotInfo> processes)
    {
        var sameSidProcesses = processes.Where(p =>
            string.Equals(p.Sid, identity.Sid, StringComparison.OrdinalIgnoreCase));

        var sameSidKeepers = sameSidProcesses.Where(p =>
            string.Equals(NormalizeImagePath(p.ImagePath), identity.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        var exitScan = KeeperExitHelper.Evaluate(sameSidProcesses.Select(process =>
            new KeeperExitProcess<int>(
                process.ProcessId,
                string.Equals(NormalizeImagePath(process.ImagePath), identity.ExecutablePath, StringComparison.OrdinalIgnoreCase),
                process.ImagePath)));

        var newestKeeper = sameSidKeepers
            .OrderByDescending(p => p.CreationTimeUtcTicks ?? long.MinValue)
            .ThenByDescending(p => p.ProcessId)
            .FirstOrDefault();

        return new ProfileKeeperScanResult(
            IsNewestKeeper: newestKeeper == null || newestKeeper.ProcessId == identity.ProcessId,
            SameSidBlockingProcessCount: exitScan.BlockingProcessCount,
            IgnorableProcessIds: exitScan.IgnorableProcessIds);
    }

    private static string? NormalizeImagePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return null;

        try
        {
            return Path.GetFullPath(imagePath);
        }
        catch
        {
            return imagePath;
        }
    }
}
