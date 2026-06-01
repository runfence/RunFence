namespace RunFence.Launching.Processes;

public interface IProcessIntegrityLevelReader
{
    int? GetIntegrityLevel(int processId);
}
