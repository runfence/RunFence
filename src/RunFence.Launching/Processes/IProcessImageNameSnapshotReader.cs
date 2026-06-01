namespace RunFence.Launching.Processes;

public interface IProcessImageNameSnapshotReader
{
    IReadOnlyList<LightweightProcessInfo> GetProcessesByImageName(string imageName);
}
