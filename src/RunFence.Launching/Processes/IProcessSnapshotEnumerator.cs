namespace RunFence.Launching.Processes;

public interface IProcessSnapshotEnumerator
{
    IReadOnlyList<ProcessSnapshotEntry> GetProcesses();
}
