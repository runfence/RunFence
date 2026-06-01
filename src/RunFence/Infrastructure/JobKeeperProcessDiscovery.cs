using System.Security.Principal;
using RunFence.Core;
using RunFence.Launching.Processes;

namespace RunFence.Infrastructure;

public sealed class JobKeeperProcessDiscovery(
    string jobKeeperExePath,
    IProcessImageNameSnapshotReader processImageNameReader,
    IProcessOwnerInfoReader processOwnerReader,
    IProcessIntegrityLevelReader processIntegrityLevelReader,
    IProcessExecutablePathReader processPathReader) : IJobKeeperProcessDiscovery
{
    public int? FindRunningJobKeeperPid(SecurityIdentifier targetUserSid, bool isLow)
    {
        foreach (var process in processImageNameReader.GetProcessesByImageName(PathConstants.JobKeeperExeName))
        {
            try
            {
                if (!IsJobKeeperProcess(process.ProcessId))
                    continue;

                var owner = processOwnerReader.GetProcessOwner(process.ProcessId, targetUserSid.Value);
                if (owner.Match != ProcessOwnerMatch.ExpectedOwner)
                    continue;

                var il = processIntegrityLevelReader.GetIntegrityLevel(process.ProcessId)
                         ?? NativeTokenHelper.MandatoryLevelMedium;
                if ((il <= NativeTokenHelper.MandatoryLevelLow) != isLow)
                    continue;

                return process.ProcessId;
            }
            catch
            {
            }
        }

        return null;
    }

    private bool IsJobKeeperProcess(int pid)
    {
        try
        {
            var imagePath = processPathReader.GetExecutablePath(pid);
            return string.Equals(imagePath, jobKeeperExePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
