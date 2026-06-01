namespace RunFence.Launching.Processes;

public sealed class ProcessSnapshotReader(
    IProcessSnapshotEnumerator processEnumerator,
    IProcessOwnerInfoReader processOwnerReader,
    IProcessExecutablePathReader processPathReader) : IProcessSnapshotReader
{
    public IReadOnlyList<ProcessSnapshotInfo> GetProcesses()
    {
        var result = new List<ProcessSnapshotInfo>();
        foreach (var process in processEnumerator.GetProcesses())
        {
            if (process.ProcessId <= 4)
                continue;

            var owner = processOwnerReader.GetProcessOwner(process.ProcessId, string.Empty);
            if (owner.OwnerSid == null)
                continue;

            result.Add(new ProcessSnapshotInfo(
                process.ProcessId,
                owner.OwnerSid,
                processPathReader.GetExecutablePath(process.ProcessId),
                process.CreationTimeUtcTicks));
        }

        return result;
    }
}
