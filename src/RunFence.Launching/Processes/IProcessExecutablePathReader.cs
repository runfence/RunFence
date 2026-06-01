namespace RunFence.Launching.Processes;

public interface IProcessExecutablePathReader
{
    string? GetExecutablePath(int processId);
}
