namespace RunFence.Launching.Processes;

public interface IProcessSnapshotReader
{
    IReadOnlyList<ProcessSnapshotInfo> GetProcesses();
}
