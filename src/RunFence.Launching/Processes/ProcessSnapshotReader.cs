using System.Diagnostics;
using System.Text;
using RunFence.Launching.Windows;

namespace RunFence.Launching.Processes;

public sealed class ProcessSnapshotReader : IProcessSnapshotReader
{
    public IReadOnlyList<ProcessSnapshotInfo> GetProcesses()
    {
        var result = new List<ProcessSnapshotInfo>();
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                if (process.Id <= 4)
                    continue;

                try
                {
                    using var processHandle = ProcessInspectionNative.OpenProcess(
                        ProcessInspectionNative.ProcessQueryLimitedInformation,
                        false,
                        (uint)process.Id);
                    if (processHandle.IsInvalid)
                        continue;

                    var sid = ProcessInspectionNative.GetTokenUserSid(processHandle.DangerousGetHandle());
                    if (sid == null)
                        continue;

                    string? imagePath = null;
                    var buffer = new StringBuilder(1024);
                    uint size = (uint)buffer.Capacity;
                    if (ProcessInspectionNative.QueryFullProcessImageName(
                            processHandle.DangerousGetHandle(),
                            0,
                            buffer,
                            ref size))
                    {
                        imagePath = buffer.ToString();
                    }

                    long? creationTimeUtcTicks = null;
                    if (ProcessInspectionNative.GetProcessTimes(
                            processHandle.DangerousGetHandle(),
                            out var creationTime,
                            out _,
                            out _,
                            out _))
                    {
                        creationTimeUtcTicks = DateTime.FromFileTimeUtc(creationTime.ToLong()).Ticks;
                    }

                    result.Add(new ProcessSnapshotInfo(process.Id, sid, imagePath, creationTimeUtcTicks));
                }
                catch
                {
                }
            }
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }

        return result;
    }
}
