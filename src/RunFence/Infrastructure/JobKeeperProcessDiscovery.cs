using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using RunFence.Core;

namespace RunFence.Infrastructure;

public sealed class JobKeeperProcessDiscovery(string jobKeeperExePath) : IJobKeeperProcessDiscovery
{
    public int? FindRunningJobKeeperPid(SecurityIdentifier targetUserSid, bool isLow)
    {
        var processes = Process.GetProcessesByName("RunFence.JobKeeper");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (!IsJobKeeperProcess(process.Id))
                        continue;

                    var ownerSid = NativeTokenHelper.TryGetProcessOwnerSid((uint)process.Id);
                    if (ownerSid == null || !targetUserSid.Equals(ownerSid))
                        continue;

                    var il = NativeTokenHelper.TryGetProcessIntegrityLevel((uint)process.Id)
                             ?? NativeTokenHelper.MandatoryLevelMedium;
                    if ((il <= NativeTokenHelper.MandatoryLevelLow) != isLow)
                        continue;

                    return process.Id;
                }
                catch { }
            }
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }

        return null;
    }

    private bool IsJobKeeperProcess(int pid)
    {
        try
        {
            using var handle = ProcessNative.OpenProcess(ProcessNative.ProcessQueryLimitedInformation, false, (uint)pid);
            if (handle.IsInvalid)
                return false;

            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            if (!ProcessNative.QueryFullProcessImageName(handle, 0, sb, ref size))
                return false;

            return string.Equals(sb.ToString(), jobKeeperExePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
